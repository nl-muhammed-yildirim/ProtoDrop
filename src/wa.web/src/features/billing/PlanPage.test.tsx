// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import PlanPage from './PlanPage'
import App from '../../App'
import { clearSession, writeSession } from '../../core/auth/session'
import type { StoredProfile } from '../../core/auth/session'
import { installMatchMediaStub } from '../../test/matchMediaStub'
import type { PlanUsageDto } from './planUsage'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

const GB = 1024 * 1024 * 1024

function installLocalStorageShim(): void {
  const store = new Map<string, string>()
  const ls: Storage = {
    get length() { return store.size },
    clear: () => store.clear(),
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => { store.delete(k) },
    setItem: (k: string, v: string) => { store.set(k, String(v)) },
  }
  Object.defineProperty(window, 'localStorage', { value: ls, configurable: true })
}

interface Call { url: string; init?: RequestInit }

function stubFetch(responder: (call: Call) => Response): Call[] {
  const calls: Call[] = []
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const call = { url: typeof input === 'string' ? input : String(input), init }
    calls.push(call)
    return responder(call)
  })
  return calls
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status })
}

function usage(partial: Partial<PlanUsageDto> = {}): PlanUsageDto {
  return {
    planCode: 'free',
    storage: { usedBytes: 1 * GB, allowedBytes: 5 * GB },
    activeTransfers: { used: 3, allowed: 20 },
    ...partial,
  }
}

async function render(onRedirect?: (url: string) => void): Promise<{ host: HTMLElement; cleanup: () => void }> {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  await act(async () => {
    void root.render(createElement(PlanPage, { onRedirect }))
    await Promise.resolve()
  })
  await act(async () => { await Promise.resolve() })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

beforeEach(() => {
  installLocalStorageShim()
  installMatchMediaStub(false)
  clearSession()
})

afterEach(() => {
  vi.unstubAllGlobals()
  window.history.pushState('', '', '/')
})

async function click(el: Element): Promise<void> {
  await act(async () => {
    el.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await Promise.resolve()
    await Promise.resolve()
  })
}

describe('PlanPage (US-020-03)', () => {
  it('Free 3/20 and 1 GB of 5 GB: plan name, two meters, Upgrade and interval radios; GET carries credentials (AC-020-3)', async () => {
    const calls = stubFetch(() => json(usage()))
    const { host, cleanup } = await render()

    expect(calls[0].url).toBe('http://localhost:8080/api/v1/billing/plan')
    expect(calls[0].init?.credentials).toBe('include')
    expect(host.textContent).toContain('Current plan: Free')
    const meters = host.querySelectorAll('[role="meter"]')
    expect(meters).toHaveLength(2)
    expect(meters[0].getAttribute('aria-valuetext')).toBe('1.00 GB of 5.00 GB')
    expect(meters[1].getAttribute('aria-valuetext')).toBe('3 of 20')
    expect(meters[1].getAttribute('aria-valuenow')).toBe('3')
    expect(meters[1].getAttribute('aria-valuemax')).toBe('20')
    expect(host.querySelector('.bp-upgrade')).not.toBeNull()
    expect(host.querySelectorAll('input[type="radio"]')).toHaveLength(2)
    expect(host.querySelector('.bp-upgrade-highlight')).toBeNull()
    cleanup()
  })

  it('Business with unlimited active transfers: shows the infinity sign, no Upgrade, no radios', async () => {
    stubFetch(() => json(usage({
      planCode: 'business',
      storage: { usedBytes: 0, allowedBytes: 1024 * GB },
      activeTransfers: { used: 500, allowed: null },
    })))
    const { host, cleanup } = await render()

    expect(host.textContent).toContain('Current plan: Business')
    const active = host.querySelector('[data-testid="bp-meter-active"]')!
    expect(active.getAttribute('aria-valuetext')).toBe('500 of ∞')
    expect(active.hasAttribute('aria-valuemax')).toBe(false)
    expect(host.querySelector('.bp-upgrade')).toBeNull()
    expect(host.querySelectorAll('input[type="radio"]')).toHaveLength(0)
    cleanup()
  })

  it('Free at 20/20: active meter full, Upgrade highlighted, at-limit status line shown', async () => {
    stubFetch(() => json(usage({ activeTransfers: { used: 20, allowed: 20 } })))
    const { host, cleanup } = await render()

    expect(host.querySelector('[data-testid="bp-meter-active"]')!.classList.contains('bp-meter-full')).toBe(true)
    expect(host.querySelector('.bp-upgrade')!.classList.contains('bp-upgrade-highlight')).toBe(true)
    expect(host.querySelector('[role="status"]')!.textContent).toContain('plan limit')
    cleanup()
  })

  it('over quota 100/20: active meter carries bp-meter-over', async () => {
    stubFetch(() => json(usage({ activeTransfers: { used: 100, allowed: 20 } })))
    const { host, cleanup } = await render()

    const active = host.querySelector('[data-testid="bp-meter-active"]')!
    expect(active.classList.contains('bp-meter-over')).toBe(true)
    expect(active.getAttribute('aria-valuenow')).toBe('20')
    cleanup()
  })

  it('Free + Annual + Upgrade: POST /billing/checkout with planCode pro and interval annual, then onRedirect(url)', async () => {
    const calls = stubFetch((c) => (c.url.endsWith('/billing/checkout') ? json({ url: 'https://checkout.stripe.test/s1', sessionId: 's1' }) : json(usage())))
    const onRedirect = vi.fn()
    const { host, cleanup } = await render(onRedirect)

    const annual = host.querySelector('input[value="annual"]') as HTMLInputElement
    await click(annual)
    await click(host.querySelector('.bp-upgrade')!)

    const post = calls.find((c) => c.url.endsWith('/billing/checkout'))!
    expect(post.init?.method).toBe('POST')
    expect(post.init?.credentials).toBe('include')
    expect(post.init?.body).toBe('{"planCode":"pro","interval":"annual"}')
    expect(onRedirect).toHaveBeenCalledWith('https://checkout.stripe.test/s1')
    cleanup()
  })

  it('Pro + Upgrade: GET /billing/portal then onRedirect(url); a null url shows the upgrade-failed alert', async () => {
    let portalUrl: string | null = 'https://billing.stripe.test/p/1'
    const calls = stubFetch((c) => (c.url.endsWith('/billing/portal') ? json({ url: portalUrl, fallback: portalUrl ? null : 'upgrade' }) : json(usage({ planCode: 'pro' }))))
    const onRedirect = vi.fn()
    const { host, cleanup } = await render(onRedirect)

    expect(host.querySelectorAll('input[type="radio"]')).toHaveLength(0)
    await click(host.querySelector('.bp-upgrade')!)
    expect(calls.some((c) => c.url.endsWith('/billing/portal'))).toBe(true)
    expect(onRedirect).toHaveBeenCalledWith('https://billing.stripe.test/p/1')

    portalUrl = null
    onRedirect.mockClear()
    await click(host.querySelector('.bp-upgrade')!)
    expect(onRedirect).not.toHaveBeenCalled()
    expect(host.querySelector('[role="alert"]')!.textContent).toContain('Could not start the upgrade')
    cleanup()
  })

  it('GET 500 shows the load-failed alert', async () => {
    stubFetch(() => new Response(null, { status: 500 }))
    const { host, cleanup } = await render()

    expect(host.querySelector('[role="alert"]')!.textContent).toContain('Could not load your plan')
    expect(host.querySelector('[role="meter"]')).toBeNull()
    cleanup()
  })

  it('full App: signed-in session at /account/plan renders PlanPage, not AuthPage', async () => {
    stubFetch(() => json(usage()))
    const profile: StoredProfile = { userId: 'u1', email: 'a@b.c', displayName: 'A', theme: 'system', planCode: 'free' }
    writeSession(profile)
    window.history.pushState('', '', '/account/plan')

    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    await act(async () => {
      void root.render(createElement(App))
      await Promise.resolve()
    })
    await act(async () => { await Promise.resolve() })

    expect(host.querySelector('.bp')).not.toBeNull()
    expect(host.querySelector('[role="meter"]')).not.toBeNull()
    root.unmount()
    host.remove()
  })
})
