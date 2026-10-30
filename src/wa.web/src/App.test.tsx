// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { renderToStaticMarkup } from 'react-dom/server'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'

import App from './App'
import { writeSession, clearSession } from './core/auth/session'

// The jsdom environment here has no localStorage. Provide a minimal in-memory shim so the
// client-side session persistence (App's lazy-init readSession / TopBar switch) is testable
// without touching production code. This is a test-only polyfill.
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

beforeAll(() => installLocalStorageShim())

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// Client mount (not renderToStaticMarkup): the session is read in a useState lazy initializer that
// touches window.localStorage — SSR renders the guest state. The landing assertion below stays
// renderToStaticMarkup per the plan's "must stay green" note; only the signed-in TopBar test needs
// the client mount so writeSession() → App's lazy initializer sees the stored profile (jsdom has window).
function renderClientApp(): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(App))
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

beforeEach(() => {
  clearSession()
})

describe('App landing', () => {
  it('renders the drop zone heading and ARIA (F-TRF-001)', () => {
    const html = renderToStaticMarkup(<App />)
    expect(html).toContain('<h1 class="dz-title">Send your files.</h1>')
    expect(html).toContain('aria-label="Upload files"')
  })

  it('guest (no session in localStorage) renders the "Sign in" TopBar button (T-008-03)', () => {
    const html = renderToStaticMarkup(<App />)
    expect(html).toContain('<button type="button" class="tb-signin"')
  })

  it('signed-in (session in localStorage) renders the initial-letter avatar and NO "Sign in" button (T-008-03)', () => {
    writeSession({ userId: 'u1', email: 'a@b.c', displayName: 'Ada Lovelace', theme: 'system', planCode: 'free' })
    const { host, cleanup } = renderClientApp()
    expect(host.innerHTML).toContain('<button type="button" class="tb-avatar"')
    expect(host.innerHTML).not.toContain('class="tb-signin"')
    cleanup()
  })
})
