// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import OrgNameSection from './OrgNameSection'

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

function mount(brandingEnabled: boolean | null = true): { host: HTMLElement; unmount: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => { void root.render(createElement(OrgNameSection, { brandingEnabled })) })
  return { host, unmount: () => { act(() => root.unmount()); host.remove() } }
}

async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })
}

function setValue(el: HTMLInputElement, value: string): void {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
  act(() => {
    setter?.call(el, value)
    el.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

beforeEach(() => {
  responses = []
  stubFetch()
})

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

describe('OrgNameSection (F-PRF-001 FR-021-1)', () => {
  it('renders the saved organization name from GET /auth/me', async () => {
    responses = [json(200, { orgName: 'Studio Nova' })]
    const { host, unmount } = mount()
    await flush()

    const input = host.querySelector('#pf-org-name') as HTMLInputElement
    expect(input.value).toBe('Studio Nova')
    expect(input.maxLength).toBe(60)
    expect(input.disabled).toBe(false)
    unmount()
  })

  it('sends orgName in the PATCH body (trimmed) and shows the saved value', async () => {
    responses = [json(200, { orgName: null }), json(200, { orgName: 'Studio Nova' })]
    const { host, unmount } = mount()
    await flush()

    setValue(host.querySelector('#pf-org-name') as HTMLInputElement, '  Studio Nova ')
    const save = host.querySelector('.pf-save-btn') as HTMLButtonElement
    await act(async () => {
      save.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()
    })

    const patch = calls[1]
    expect(patch.init?.method).toBe('PATCH')
    expect(patch.init?.credentials).toBe('include')
    expect(patch.init?.body).toBe(JSON.stringify({ orgName: 'Studio Nova' }))
    expect((host.querySelector('#pf-org-name') as HTMLInputElement).value).toBe('Studio Nova')
    unmount()
  })

  it('sends null when the field is cleared', async () => {
    responses = [json(200, { orgName: 'Studio Nova' }), json(200, { orgName: null })]
    const { host, unmount } = mount()
    await flush()

    setValue(host.querySelector('#pf-org-name') as HTMLInputElement, '   ')
    await act(async () => {
      ;(host.querySelector('.pf-save-btn') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(calls[1].init?.body).toBe(JSON.stringify({ orgName: null }))
    unmount()
  })

  it('is disabled on the Free plan with the Pro plan required copy', async () => {
    responses = [json(200, { orgName: null })]
    const { host, unmount } = mount(false)
    await flush()

    const input = host.querySelector('#pf-org-name') as HTMLInputElement
    expect(input.disabled).toBe(true)
    expect((host.querySelector('.pf-save-btn') as HTMLButtonElement).disabled).toBe(true)
    expect(host.textContent).toContain('Pro plan required')
    expect(calls).toHaveLength(1)
    unmount()
  })

  it('shows an alert when the server rejects the save', async () => {
    responses = [json(200, { orgName: null }), json(400, { code: 'VALIDATION' })]
    const { host, unmount } = mount()
    await flush()

    setValue(host.querySelector('#pf-org-name') as HTMLInputElement, 'Studio')
    await act(async () => {
      ;(host.querySelector('.pf-save-btn') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(host.querySelector('[role="alert"]')).not.toBeNull()
    unmount()
  })

  it('renders nothing while branding is unknown', async () => {
    responses = [json(200, { orgName: 'Studio Nova' })]
    const { host, unmount } = mount(null)
    await flush()

    expect(host.querySelector('#pf-org-name')).toBeNull()
    unmount()
  })

  it('renders nothing when the read fails', async () => {
    responses = [() => new Response(null, { status: 500 })]
    const { host, unmount } = mount()
    await flush()

    expect(host.querySelector('#pf-org-name')).toBeNull()
    unmount()
  })
})
