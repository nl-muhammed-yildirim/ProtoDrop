// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import OrgLogoSection from './OrgLogoSection'

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

const json = (status: number, body: unknown) => () => new Response(JSON.stringify(body), { status })
const status = (code: number) => () => new Response(null, { status: code })

// Small server-provided cap (test data, not a product limit).
const CAP = 2048
const pro = (logoUrl: string | null) => json(200, { logoUrl, maxLogoSize: CAP, brandingEnabled: true })

function mount(): { host: HTMLElement; unmount: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => { void root.render(createElement(OrgLogoSection)) })
  return { host, unmount: () => { act(() => root.unmount()); host.remove() } }
}

async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })
}

function click(el: Element): Promise<void> {
  return act(async () => {
    el.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await Promise.resolve()
    await Promise.resolve()
  })
}

async function pick(host: HTMLElement, file: File): Promise<void> {
  const input = host.querySelector('input[type="file"]') as HTMLInputElement
  Object.defineProperty(input, 'files', { value: [file], configurable: true })
  await act(async () => {
    input.dispatchEvent(new Event('change', { bubbles: true }))
    await Promise.resolve()
    await Promise.resolve()
    await Promise.resolve()
  })
}

beforeEach(() => {
  responses = []
  stubFetch()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('OrgLogoSection (US-021-04)', () => {
  it('renders the preview and Remove button when a logo exists', async () => {
    responses = [pro('https://blob.test/avatars/u/logo?sig=x')]
    const { host, unmount } = mount()
    await flush()
    expect(calls[0].url).toContain('/auth/me/logo')
    expect(calls[0].init?.credentials).toBe('include')
    expect(host.querySelector('img.pf-logo-preview')?.getAttribute('src')).toBe('https://blob.test/avatars/u/logo?sig=x')
    expect(host.querySelector('.au-ghost-btn')?.textContent).toBe('Remove logo')
    unmount()
  })

  it('Free plan: drop zone is aria-disabled with the Pro title and sends no PUT', async () => {
    responses = [json(200, { logoUrl: null, maxLogoSize: CAP, brandingEnabled: false })]
    const { host, unmount } = mount()
    await flush()
    const drop = host.querySelector('.pf-logo-drop') as HTMLElement
    expect(drop.getAttribute('aria-disabled')).toBe('true')
    expect(drop.getAttribute('title')).toBe('Pro plan required')
    await click(drop)
    await pick(host, new File(['x'], 'a.png', { type: 'image/png' }))
    expect(calls.filter((c) => c.init?.method === 'PUT')).toHaveLength(0)
    unmount()
  })

  it('oversize file: alert names the formatted limit and no PUT is sent', async () => {
    responses = [pro(null)]
    const { host, unmount } = mount()
    await flush()
    await pick(host, new File([new Uint8Array(CAP + 1)], 'big.png', { type: 'image/png' }))
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('2.00 KB')
    expect(calls.filter((c) => c.init?.method === 'PUT')).toHaveLength(0)
    unmount()
  })

  it('PUT 200 updates the preview', async () => {
    responses = [pro(null), pro('https://blob.test/avatars/u/logo?sig=new')]
    const { host, unmount } = mount()
    await flush()
    await pick(host, new File(['png'], 'a.png', { type: 'image/png' }))
    expect(calls[1].init?.method).toBe('PUT')
    expect((calls[1].init?.headers as Record<string, string>)['Content-Type']).toBe('image/png')
    expect(host.querySelector('img.pf-logo-preview')?.getAttribute('src')).toBe('https://blob.test/avatars/u/logo?sig=new')
    unmount()
  })

  it('PUT 415 shows the type message', async () => {
    responses = [pro(null), status(415)]
    const { host, unmount } = mount()
    await flush()
    await pick(host, new File(['gif'], 'a.gif', { type: 'image/gif' }))
    expect(host.querySelector('[role="alert"]')?.textContent).toBe('Use a PNG, JPEG, WebP or SVG image.')
    unmount()
  })

  it('Remove sends DELETE and drops the preview', async () => {
    responses = [pro('https://blob.test/avatars/u/logo?sig=x'), status(204)]
    const { host, unmount } = mount()
    await flush()
    await click(host.querySelector('.au-ghost-btn')!)
    expect(calls[1].init?.method).toBe('DELETE')
    expect(host.querySelector('img.pf-logo-preview')).toBeNull()
    expect(host.querySelector('.au-ghost-btn')).toBeNull()
    unmount()
  })
})
