// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { renderToStaticMarkup } from 'react-dom/server'
import { beforeAll, beforeEach, afterEach, describe, expect, it } from 'vitest'

import App from './App'
import { writeSession, clearSession, readSession } from './core/auth/session'
// T-015-02 (FR-015-4): jsdom has no window.matchMedia — the useSystemTheme effect needs a controllable stub.
import type { MatchMediaStub } from './test/matchMediaStub'
import { installMatchMediaStub } from './test/matchMediaStub'
// T-015-02: mirrors index.html's pre-paint script (jsdom never runs it) to seed each test's initial data-theme.
import { resolveTheme } from './core/theme/resolveTheme'

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

// T-015-02 (FR-015-4): jsdom has no window.matchMedia — App's useSystemTheme effect needs a controllable stub.
let mqStub: MatchMediaStub
beforeAll(() => {
  installLocalStorageShim()
  mqStub = installMatchMediaStub(false)   // initial OS preference: light (matches=false).
})

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

// T-015-02: data-theme lives on the shared jsdom documentElement — start each test clean (jsdom's initial state)
// and leave it clean so other files' tests don't inherit a leftover value.
beforeEach(() => { document.documentElement.removeAttribute('data-theme') })

function setNativeSelectValue(el: HTMLSelectElement, value: string): void {
  const proto = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')
  ;(proto?.set as ((v: string) => void) | undefined)?.call(el, value)
  el.dispatchEvent(new Event('change', { bubbles: true }))
}

afterEach(() => { document.documentElement.removeAttribute('data-theme') })

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

describe('App SPA 404 (T-013-02, AC-013-4)', () => {
  it('unknown route renders the 404 screen copy + one action, no stack trace or raw code visible', () => {
    // Set pathname to an unknown route before rendering.
    window.history.pushState('', '', '/nonexistent-route')
    const html = renderToStaticMarkup(<App />)
    expect(html).toContain('Oops — this page moved.')
    expect(html).toContain('Go to home')
    // No stack trace or raw code visible.
    expect(html).not.toContain('ErrorBoundary')
    expect(html).not.toContain('componentDidCatch')
    // Reset pathname for other tests.
    window.history.pushState('', '', '/')
  })

  it("'/' still renders landing (regression guard)", () => {
    const html = renderToStaticMarkup(<App />)
    expect(html).toContain('<h1 class="dz-title">Send your files.</h1>')
  })
})

describe('App live OS theme tracking (T-015-02, FR-015-4 / AC-015-3)', () => {
  // jsdom never runs index.html's pre-paint script — mirror it here so each test starts from the same
  // state a real browser load would produce (explicit attribute before React mounts).
  function simulatePrePaintBootstrap(stored: 'system' | 'light' | 'dark' | undefined): void {
    document.documentElement.setAttribute('data-theme', resolveTheme(stored, mqStub.matches))
  }

  it("guest + OS dark → data-theme=\"dark\" live without reload (AC-015-3)", async () => {
    mqStub.flipMatches(false)   // initial OS preference: light.
    simulatePrePaintBootstrap(undefined)   // guest — index.html resolves the OS preference pre-paint.
    const { cleanup } = renderClientApp()

    expect(document.documentElement.getAttribute('data-theme')).toBe('light')   // bootstrap state before any change.

    mqStub.flipMatches(true)  // user switches their OS to dark with the tab open…
    await act(async () => { mqStub.fireChange() })

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')   // …the listener did it live, no reload (AC-015-3).
    cleanup()
  })

  it("explicit light does NOT follow an OS change to dark (EC-015-1)", async () => {
    mqStub.flipMatches(false)  // initial OS preference: light.
    writeSession({ userId: 'u1', email: 'a@b.c', displayName: 'Ada Lovelace', theme: 'light', planCode: 'free' })
    simulatePrePaintBootstrap('light')   // index.html honors the stored explicit choice pre-paint.
    const { cleanup } = renderClientApp()

    expect(document.documentElement.getAttribute('data-theme')).toBe('light')   // bootstrap state before any change.

    mqStub.flipMatches(true)                  // OS flips to dark while in explicit mode…
    await act(async () => { mqStub.fireChange() })

    expect(document.documentElement.getAttribute('data-theme')).toBe('light')  // …and the app stays light (EC-015-1).
    cleanup()
  })

  it("explicit dark does NOT follow an OS change to light (EC-015-1)", async () => {
    mqStub.flipMatches(true)   // initial OS preference: dark.
    writeSession({ userId: 'u1', email: 'a@b.c', displayName: 'Ada Lovelace', theme: 'dark', planCode: 'free' })
    simulatePrePaintBootstrap('dark')   // index.html honors the stored explicit choice pre-paint.
    const { cleanup } = renderClientApp()

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')   // bootstrap state before any change.

    mqStub.flipMatches(false)                // OS flips to light while in explicit mode…
    await act(async () => { mqStub.fireChange() })

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')   // …and the app stays dark (EC-015-1).
    cleanup()
  })

  it("TopBar-initiated theme change persists to the session mirror so the next paint's bootstrap reads it (AC-015-1 client half)", async () => {
    writeSession({ userId: 'u1', email: 'a@b.c', displayName: 'Ada Lovelace', theme: 'system', planCode: 'free' })
    const { host, cleanup } = renderClientApp()

    const avatar = host.querySelector('.tb-avatar') as HTMLButtonElement
    act(() => { avatar.dispatchEvent(new MouseEvent('click', { bubbles: true })) })
    const select = host.querySelector('.tb-theme-select') as HTMLSelectElement
    await act(async () => {
      setNativeSelectValue(select, 'dark')   // TopBar's select fires only onThemeChange — App persists the mirror.
    })

    expect(readSession()?.theme).toBe('dark')   // index.html's pre-paint bootstrap will resolve dark before first paint next load (AC-015-1).
    cleanup()
  })
})
