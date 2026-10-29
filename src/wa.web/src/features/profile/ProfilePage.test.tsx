// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import ProfilePage from './ProfilePage'
import App from '../../App'
import type { StoredProfile } from '../../core/auth/session'
import { clearSession, writeSession } from '../../core/auth/session'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// jsdom's built-in localStorage is present, but App.test.tsx installs an in-memory shim for the
// signed-in App tests; mirror that so writeSession/readSession round-trip deterministically.
function installLocalStorageShim(): void {
  if (typeof window === 'undefined') return
  const store = new Map<string, string>()
  const ls: Storage = {
    get length() { return store.size },
    clear: () => store.clear(),
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => { store.delete(k); },
    setItem: (k: string, v: string) => { store.set(k, String(v)); },
  }
  Object.defineProperty(window, 'localStorage', { value: ls, configurable: true })
}

function renderProfilePage(
  profile: StoredProfile,
  onSignedIn?: (p: StoredProfile) => void,
  onThemeChange?: (t: 'system' | 'light' | 'dark') => void,
): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(
      createElement(ProfilePage, {
        profile,
        onSignedIn: onSignedIn ?? (() => {}),
        onThemeChange: onThemeChange ?? (() => {}),
      }),
    )
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

// Mirror AuthPage.test.tsx: hand-stub fetch with a capture closure (no extra packages).
function stubFetch(
  captured: { url?: string; init?: RequestInit },
  responder?: (req: Request) => Response | Promise<Response>,
): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      captured.url = typeof input === 'string' ? input : String(input)
      captured.init = init
      const req = new Request(typeof input === 'string' ? input : String(input), { method: init?.method ?? 'GET' })
      return responder ? await responder(req) : new Response(null, { status: 200 })
    },
  )
}

function setNativeValue(el: HTMLInputElement | HTMLTextAreaElement, value: string): void {
  const proto = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')
  ;(proto?.set as ((v: string) => void) | undefined)?.call(el, value)
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

function setNativeSelectValue(el: HTMLSelectElement, value: string): void {
  const proto = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')
  ;(proto?.set as ((v: string) => void) | undefined)?.call(el, value)
  el.dispatchEvent(new Event('change', { bubbles: true }))
}

const BASE_PROFILE: StoredProfile = { userId: 'u1', email: 'a@b.c', displayName: 'A', theme: 'system', planCode: 'free' }

beforeEach(() => {
  installLocalStorageShim()
  clearSession()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('ProfilePage (T-008-07)', () => {
  it('save displayName happy path: PATCH /auth/me { displayName } with credentials + onSignedIn sync (AC-008-3 client half)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    stubFetch(captured, () => {
      return Promise.resolve(
        new Response(JSON.stringify({ ...BASE_PROFILE, displayName: 'Ada Lovelace' }), { status: 200 }),
      )
    })

    const onSignedIn = vi.fn()
    const { host, cleanup } = renderProfilePage(BASE_PROFILE, onSignedIn)

    const nameInput = host.querySelector('#pf-name') as HTMLInputElement
    setNativeValue(nameInput, 'Ada Lovelace')

    const saveBtn = Array.from(host.querySelectorAll('.pf-save-btn'))[0] as HTMLButtonElement
    await act(async () => {
      saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    cleanup()
    expect(captured.url).toContain('/auth/me')
    expect(captured.init?.method).toBe('PATCH')
    expect(captured.init?.credentials).toBe('include')
    expect(captured.init?.body).toBe(JSON.stringify({ displayName: 'Ada Lovelace' }))
    expect(onSignedIn).toHaveBeenCalledTimes(1)
    expect(onSignedIn).toHaveBeenCalledWith({ ...BASE_PROFILE, displayName: 'Ada Lovelace' })
  })

  it('empty display name save: input shows the server fallback value "ada" (email local part), never blank (exit check d)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    stubFetch(captured, () => {
      // Server falls back an empty displayName to the email's local part.
      return Promise.resolve(
        new Response(JSON.stringify({ ...BASE_PROFILE, displayName: 'ada' }), { status: 200 }),
      )
    })

    const onSignedIn = vi.fn()
    const { host, cleanup } = renderProfilePage(BASE_PROFILE, onSignedIn)

    // Blank the input (spaces only → trimmed to empty).
    const nameInput = host.querySelector('#pf-name') as HTMLInputElement
    setNativeValue(nameInput, '   ')

    const saveBtn = Array.from(host.querySelectorAll('.pf-save-btn'))[0] as HTMLButtonElement
    await act(async () => {
      saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    // Input now holds the SAVED server value, never blank.
    expect(nameInput.value).toBe('ada')
    cleanup()
  })

  it('theme change to dark: data-theme="dark" on documentElement + PATCH { theme } (F-TRF-015)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    stubFetch(captured, () => {
      return Promise.resolve(
        new Response(JSON.stringify({ ...BASE_PROFILE, theme: 'dark' }), { status: 200 }),
      )
    })

    const onSignedIn = vi.fn()
    const { host, cleanup } = renderProfilePage(BASE_PROFILE, onSignedIn)

    const select = host.querySelector('#pf-theme') as HTMLSelectElement
    await act(async () => {
      setNativeSelectValue(select, 'dark')
      await Promise.resolve()
    })

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    cleanup()
    expect(captured.url).toContain('/auth/me')
    expect(captured.init?.method).toBe('PATCH')
    expect(captured.init?.credentials).toBe('include')
    expect(captured.init?.body).toBe(JSON.stringify({ theme: 'dark' }))
    expect(onSignedIn).toHaveBeenCalledTimes(1)
    expect(onSignedIn).toHaveBeenCalledWith({ ...BASE_PROFILE, theme: 'dark' })
  })

  it('sign out from the TopBar avatar menu: POST /auth/logout with credentials → TopBar flips to "Sign in" (exit check c)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    stubFetch(captured, () => {
      return Promise.resolve(new Response(null, { status: 200 }))
    })

    // Signed-in session → App's lazy readSession() returns the profile → TopBar avatar branch.
    writeSession(BASE_PROFILE)

    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => {
      void root.render(createElement(App))
    })

    // Open the avatar dropdown.
    const avatar = host.querySelector('.tb-avatar') as HTMLButtonElement
    act(() => {
      avatar.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    // Click the "Sign out" row → App.handleSignOut fires POST /auth/logout (D-3).
    const signOutBtn = Array.from(host.querySelectorAll('.tb-dropdown-row')) as HTMLButtonElement[]
    const signOutRow = signOutBtn.find((b) => b.textContent === 'Sign out')!
    await act(async () => {
      signOutRow.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    expect(captured.url).toContain('/auth/logout')
    expect(captured.init?.method).toBe('POST')
    expect(captured.init?.credentials).toBe('include')
    // Session cleared → TopBar flips back to the guest "Sign in" button.
    expect(host.innerHTML).toContain('<button type="button" class="tb-signin"')
    root.unmount()
    host.remove()
  })
})
