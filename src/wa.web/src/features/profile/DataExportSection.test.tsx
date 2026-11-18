// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import DataExportSection, { EXPORT_POLL_MS } from './DataExportSection'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

interface Call { url: string; init?: RequestInit }

let calls: Call[]
let responses: Array<() => Response>

function stubFetch(): void {
  calls = []
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    calls.push({ url: String(input), init })
    const next = responses.shift()
    if (!next) throw new Error('unexpected fetch')
    return next()
  })
}

const noContent = () => new Response(null, { status: 204 })
const json = (status: number, body: unknown) => () => new Response(JSON.stringify(body), { status })
const processing = json(200, { state: 'processing', createdAtUtc: '2026-11-18T10:00:00Z' })

function mount(): { host: HTMLElement; unmount: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => { void root.render(createElement(DataExportSection)) })
  return { host, unmount: () => { act(() => root.unmount()); host.remove() } }
}

async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve(); await Promise.resolve() })
}

function click(el: Element): Promise<void> {
  return act(async () => {
    el.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await Promise.resolve()
    await Promise.resolve()
  })
}

beforeEach(() => {
  responses = []
  stubFetch()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('DataExportSection (US-047-01)', () => {
  it('mount GET 204: shows helper + export button, no status', async () => {
    responses = [noContent]
    const { host, unmount } = mount()
    await flush()
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toContain('/auth/me/export')
    expect(calls[0].init?.credentials).toBe('include')
    expect(host.textContent).toContain('Includes your profile and transfer history')
    expect(host.querySelector('.pf-save-btn')?.textContent).toBe('Export my data')
    expect(host.querySelector('[role="status"]')).toBeNull()
    unmount()
  })

  it('click POSTs with credentials and shows Preparing; refreshes only every 30 s', async () => {
    vi.useFakeTimers()
    responses = [noContent, json(202, { state: 'processing' }), json(200, { state: 'ready', downloadUrl: 'https://blob.test/exports/u/e.zip?sig=x' })]
    const { host, unmount } = mount()
    await flush()

    await click(host.querySelector('.pf-save-btn')!)
    expect(calls[1].init?.method).toBe('POST')
    expect(calls[1].init?.credentials).toBe('include')
    expect(calls[1].url).toContain('/auth/me/export')
    expect(host.textContent).toContain('Preparing your export…')
    expect(host.querySelector('.pf-save-btn')).toBeNull()

    await act(async () => { await vi.advanceTimersByTimeAsync(EXPORT_POLL_MS - 1) })
    expect(calls).toHaveLength(2) // no GET before 30 s
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(calls).toHaveLength(3)
    expect(calls[2].init?.method).toBeUndefined()

    // Ready: download anchor + validity caption; the timer chain stopped.
    const anchor = host.querySelector('a.pf-plan-link') as HTMLAnchorElement
    expect(anchor.getAttribute('href')).toBe('https://blob.test/exports/u/e.zip?sig=x')
    expect(anchor.hasAttribute('download')).toBe(true)
    expect(host.textContent).toContain('The link stays valid for 24 hours.')
    await act(async () => { await vi.advanceTimersByTimeAsync(EXPORT_POLL_MS * 3) })
    expect(calls).toHaveLength(3)
    unmount()
  })

  it('409 shows the in-flight message and the preparing state', async () => {
    responses = [noContent, json(409, { code: 'EXPORT_IN_FLIGHT' })]
    const { host, unmount } = mount()
    await flush()
    await click(host.querySelector('.pf-save-btn')!)
    expect(host.textContent).toContain('Your export is already preparing.')
    expect(host.textContent).toContain('Preparing your export…')
    unmount()
  })

  it('other POST failure shows an error alert and keeps the button', async () => {
    responses = [noContent, json(500, { code: 'INTERNAL' })]
    const { host, unmount } = mount()
    await flush()
    await click(host.querySelector('.pf-save-btn')!)
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Could not request your export')
    expect(host.querySelector('.pf-save-btn')).not.toBeNull()
    unmount()
  })

  it('failed state shows the failure and a Try again button', async () => {
    responses = [json(200, { state: 'failed', createdAtUtc: '2026-11-18T10:00:00Z' })]
    const { host, unmount } = mount()
    await flush()
    expect(host.textContent).toContain('Your export failed.')
    expect(host.querySelector('.pf-save-btn')?.textContent).toBe('Try again')
    unmount()
  })

  it('unmount clears the timer: no fetch after unmount', async () => {
    vi.useFakeTimers()
    responses = [processing]
    const { unmount } = mount()
    await flush()
    expect(calls).toHaveLength(1)
    unmount()
    await act(async () => { await vi.advanceTimersByTimeAsync(EXPORT_POLL_MS * 2) })
    expect(calls).toHaveLength(1)
  })
})
