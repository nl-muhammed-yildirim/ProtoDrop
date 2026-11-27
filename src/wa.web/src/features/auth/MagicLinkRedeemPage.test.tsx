// @vitest-environment jsdom
import { StrictMode, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import MagicLinkRedeemPage from './MagicLinkRedeemPage'
import type { StoredProfile } from '../../core/auth/session'
import { clearSession, readSession } from '../../core/auth/session'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// jsdom here has no usable window.localStorage — in-memory shim so writeSession/readSession round-trip.
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

const dto: StoredProfile = { userId: 'u1', email: 'a@b.c', displayName: 'A', theme: 'system', planCode: 'free' }

type Call = { url: string; init?: RequestInit }

function stubFetch(responder: () => Response | Promise<Response>): Call[] {
  const calls: Call[] = []
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    calls.push({ url: typeof input === 'string' ? input : String(input), init })
    return responder()
  })
  return calls
}

async function flush(): Promise<void> {
  await act(async () => {
    await new Promise((r) => setTimeout(r, 0))
  })
}

async function mount(token: string, onSignedIn: (p: StoredProfile) => void, strict = false) {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  const page = createElement(MagicLinkRedeemPage, { token, onSignedIn })
  act(() => {
    root.render(strict ? createElement(StrictMode, null, page) : page)
  })
  await flush()
  return { host, cleanup: () => { act(() => root.unmount()); host.remove() } }
}

const problem = (status: number, code: string, details: string) =>
  new Response(JSON.stringify({ code, status, title: details, details }), { status })

beforeEach(() => {
  installLocalStorageShim()
  clearSession()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// The page memoises one request per token for the page lifetime — every test uses a distinct token.
describe('MagicLinkRedeemPage', () => {
  it('POSTs the token once, stores the session and signs in', async () => {
    const calls = stubFetch(() => new Response(JSON.stringify(dto), { status: 200 }))
    const onSignedIn = vi.fn()
    const { cleanup } = await mount('tok-success', onSignedIn)

    expect(calls).toHaveLength(1)
    expect(calls[0].url.endsWith('/auth/magic-link/redeem')).toBe(true)
    expect(calls[0].init?.method).toBe('POST')
    expect(calls[0].init?.body).toBe('{"token":"tok-success"}')
    expect(calls[0].init?.credentials).toBe('include')
    expect(readSession()).toEqual(dto)
    expect(onSignedIn).toHaveBeenCalledTimes(1)
    expect(onSignedIn).toHaveBeenCalledWith(dto)
    cleanup()
  })

  it('410 LINK_EXPIRED shows the server copy and a way to request a new link', async () => {
    stubFetch(() => problem(410, 'LINK_EXPIRED', 'This link has expired'))
    const onSignedIn = vi.fn()
    const { host, cleanup } = await mount('tok-expired', onSignedIn)

    expect(host.querySelector('[role="alert"]')?.textContent).toBe('This link has expired')
    expect(readSession()).toBeNull()
    expect(onSignedIn).not.toHaveBeenCalled()

    const btn = host.querySelector('button')
    expect(btn?.textContent).toBe('Email me a magic link')
    act(() => { btn?.click() })
    expect(window.location.pathname).toBe('/account')
    cleanup()
  })

  it('400 VALIDATION shows the malformed-token copy', async () => {
    stubFetch(() => problem(400, 'VALIDATION', 'The provided magic link token is malformed.'))
    const { host, cleanup } = await mount('tok-malformed', vi.fn())

    expect(host.querySelector('[role="alert"]')?.textContent).toBe('The provided magic link token is malformed.')
    cleanup()
  })

  it('network failure shows the generic error', async () => {
    stubFetch(() => { throw new TypeError('network down') })
    const { host, cleanup } = await mount('tok-network', vi.fn())

    expect(host.querySelector('[role="alert"]')?.textContent).toBeTruthy()
    cleanup()
  })

  it('StrictMode double-run issues exactly one POST and one sign-in', async () => {
    const calls = stubFetch(() => new Response(JSON.stringify(dto), { status: 200 }))
    const onSignedIn = vi.fn()
    const { cleanup } = await mount('tok-strict', onSignedIn, true)

    expect(calls).toHaveLength(1)
    expect(onSignedIn).toHaveBeenCalledTimes(1)
    cleanup()
  })

  it('replaces (not pushes) the history entry and never replays a consumed success on Back', async () => {
    window.history.replaceState(null, '', '/auth?ml=tok-back')
    const replaceSpy = vi.spyOn(window.history, 'replaceState')
    const pushSpy = vi.spyOn(window.history, 'pushState')
    const calls = stubFetch(() => new Response(JSON.stringify(dto), { status: 200 }))
    const onSignedIn = vi.fn()
    const first = await mount('tok-back', onSignedIn)

    expect(onSignedIn).toHaveBeenCalledTimes(1)
    expect(replaceSpy).toHaveBeenCalledWith(null, '', '/')
    expect(pushSpy).not.toHaveBeenCalled()
    expect(window.location.pathname + window.location.search).toBe('/')
    first.cleanup()

    // Back-navigation re-mounts the page for the same token: the server (single-use) now answers 410.
    clearSession()
    calls.length = 0
    stubFetch(() => problem(410, 'LINK_EXPIRED', 'This link has expired'))
    const second = await mount('tok-back', onSignedIn)

    expect(onSignedIn).toHaveBeenCalledTimes(1)   // no replayed sign-in
    expect(readSession()).toBeNull()               // no stale session restored
    expect(second.host.querySelector('[role="alert"]')?.textContent).toBe('This link has expired')
    second.cleanup()
    replaceSpy.mockRestore()
    pushSpy.mockRestore()
  })

  it('drops ?ml= from the address bar on failure and allows a retry after a network error', async () => {
    window.history.replaceState(null, '', '/auth?ml=tok-retry')
    stubFetch(() => { throw new TypeError('network down') })
    const first = await mount('tok-retry', vi.fn())

    expect(first.host.querySelector('[role="alert"]')).not.toBeNull()
    expect(window.location.pathname + window.location.search).toBe('/auth')
    first.cleanup()

    const calls = stubFetch(() => new Response(JSON.stringify(dto), { status: 200 }))
    const onSignedIn = vi.fn()
    const second = await mount('tok-retry', onSignedIn)

    expect(calls).toHaveLength(1)   // failed outcome was not cached
    expect(onSignedIn).toHaveBeenCalledWith(dto)
    second.cleanup()
  })
})
