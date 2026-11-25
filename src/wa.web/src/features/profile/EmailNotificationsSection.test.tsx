// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import EmailNotificationsSection from './EmailNotificationsSection'
import { ME_BASE } from './ProfilePage'

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

function mount(): { host: HTMLElement; unmount: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => { void root.render(createElement(EmailNotificationsSection)) })
  return { host, unmount: () => { act(() => root.unmount()); host.remove() } }
}

async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })
}

// React reads checkbox state via the `click` event (react-dom shouldUseClickEvent); jsdom's .click() performs activation behavior (toggles checked).
function toggle(el: HTMLInputElement): void {
  act(() => { el.click() })
}

beforeEach(() => {
  responses = []
  stubFetch()
})

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

describe('EmailNotificationsSection (F-XCT-003 FR-046-2/4)', () => {
  it('renders checked for true', async () => {
    responses = [json(200, { emailNotifications: true })]
    const { host, unmount } = mount()
    await flush()

    expect((host.querySelector('#pf-notify-switch') as HTMLInputElement).checked).toBe(true)
    unmount()
  })

  it('renders unchecked for false', async () => {
    responses = [json(200, { emailNotifications: false })]
    const { host, unmount } = mount()
    await flush()

    expect((host.querySelector('#pf-notify-switch') as HTMLInputElement).checked).toBe(false)
    unmount()
  })

  it('renders checked when the field is missing (effective value; absent = on)', async () => {
    responses = [json(200, {})]
    const { host, unmount } = mount()
    await flush()

    expect((host.querySelector('#pf-notify-switch') as HTMLInputElement).checked).toBe(true)
    unmount()
  })

  it('renders nothing on GET 404', async () => {
    responses = [json(404, {})]
    const { host, unmount } = mount()
    await flush()

    expect(host.querySelector('#pf-notify-switch')).toBeNull()
    unmount()
  })

  it('toggle off → one PATCH to ME_BASE with body {"emailNotifications":false}, unchecked switch, status toast (AC-046-1)', async () => {
    responses = [json(200, { emailNotifications: true }), json(200, { emailNotifications: false })]
    const { host, unmount } = mount()
    await flush()

    toggle(host.querySelector('#pf-notify-switch') as HTMLInputElement)
    await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })

    expect(calls).toHaveLength(2) // GET + one PATCH — no extra requests (5/min auth rate rule)
    const patch = calls[1]
    expect(patch.url).toBe(ME_BASE)
    expect(patch.init?.method).toBe('PATCH')
    expect(patch.init?.credentials).toBe('include')
    expect(patch.init?.body).toBe(JSON.stringify({ emailNotifications: false }))
    expect((host.querySelector('#pf-notify-switch') as HTMLInputElement).checked).toBe(false)
    const toast = host.querySelector('[role="status"]')!
    expect(toast.textContent).toBe('Notification preferences saved.')
    unmount()
  })

  it('toggle back on → body {"emailNotifications":true} (AC-046-1 scenario 2)', async () => {
    responses = [json(200, { emailNotifications: false }), json(200, { emailNotifications: true })]
    const { host, unmount } = mount()
    await flush()

    toggle(host.querySelector('#pf-notify-switch') as HTMLInputElement)
    await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })

    expect(calls[1].init?.body).toBe(JSON.stringify({ emailNotifications: true }))
    unmount()
  })

  it('PATCH 500 → switch reverts to checked, role=alert error, no toast', async () => {
    responses = [json(200, { emailNotifications: true }), json(500, {})]
    const { host, unmount } = mount()
    await flush()

    toggle(host.querySelector('#pf-notify-switch') as HTMLInputElement) // off attempt
    await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })

    expect((host.querySelector('#pf-notify-switch') as HTMLInputElement).checked).toBe(true) // reverted to checked (previous = true)
    expect(host.querySelector('[role="alert"]')).not.toBeNull()
    expect(host.querySelector('[role="status"]')).toBeNull() // no success toast on failure
    unmount()
  })

  it('helper text contains "Recipients" and "billing" (FR-046-4)', async () => {
    responses = [json(200, {})]
    const { host, unmount } = mount()
    await flush()

    expect(host.textContent).toContain('Recipients')
    expect(host.textContent).toContain('billing')
    unmount()
  })
})
