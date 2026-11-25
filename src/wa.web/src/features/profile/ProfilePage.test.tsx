// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import ProfilePage from './ProfilePage'
import App from '../../App'
import type { StoredProfile } from '../../core/auth/session'
import { clearSession, readSession, writeSession } from '../../core/auth/session'
import { resetFeatureGates } from '../../core/features/featureGates'
import i18n from '../../core/i18n/init'
// T-015-02 (FR-015-4): jsdom has no window.matchMedia — changeTheme's resolved-set path needs a controllable stub.
import type { MatchMediaStub } from '../../test/matchMediaStub'
import { installMatchMediaStub } from '../../test/matchMediaStub'

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
  onDeleteAccount?: () => void,
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
        onDeleteAccount: onDeleteAccount ?? (() => {}),   // T-008-09: no-op default — the D-6 full-App tests cover the real handler.
      }),
    )
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

// US-047-01: body served for GET /api/v1/features — all gates off unless a test turns one on.
let featuresBody: Record<string, boolean> = {}

// Mirror AuthPage.test.tsx: hand-stub fetch with a capture closure (no extra packages).
function stubFetch(
  captured: { url?: string; init?: RequestInit },
  responder?: (req: Request) => Response | Promise<Response>,
): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      // The landing page's background plan-limits load (PlanLimitsSync) is not part of what these tests capture.
      if (String(input).endsWith('/billing/plan')) return new Response(null, { status: 404 })
      // US-021-04: OrgLogoSection's mount read must not overwrite what these tests capture (render-nothing on non-ok).
      if (String(input).endsWith('/auth/me/logo')) return new Response(null, { status: 404 })
      // F-PRF-001: OrgNameSection's mount read (GET /auth/me) must not overwrite what these tests capture either.
      if (String(input).endsWith('/auth/me') && !init?.method) return new Response(null, { status: 404 })
      // US-047-01: FeatureGate's mount read of the phase gates must not overwrite what these tests capture.
      if (String(input).endsWith('/api/v1/features')) return new Response(JSON.stringify(featuresBody), { status: 200 })
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

// T-015-02 (FR-015-4): jsdom has no window.matchMedia — changeTheme's resolved-set path reads it.
let mqStub: MatchMediaStub
beforeEach(() => {
  installLocalStorageShim()
  mqStub = installMatchMediaStub(false)   // initial OS preference: light (matches=false).
  clearSession()
  featuresBody = {}
  resetFeatureGates()
})

afterEach(() => {
  resetFeatureGates()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  document.documentElement.removeAttribute('data-theme')   // shared jsdom element — keep tests hermetic.
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

  function navigateToProfile(): void {
    window.history.pushState('', '', '/account/profile')
    window.dispatchEvent(new PopStateEvent('popstate'))   // App's popstate listener updates pathname.
  }

  it('cancel: no request sent, modal closes, session unchanged (exit check d)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    stubFetch(captured, () => { throw new Error('no requests allowed on the cancel path') })

    writeSession(BASE_PROFILE)
    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => {
      void root.render(createElement(App))
    })
    await act(async () => { navigateToProfile() })

    // Open the confirm modal via the danger-zone button.
    const deleteBtn = host.querySelector('.pf-delete-btn') as HTMLButtonElement
    act(() => { deleteBtn.dispatchEvent(new MouseEvent('click', { bubbles: true })) })
    expect(host.innerHTML).toContain('pf-modal-copy')   // modal is open

    const cancelBtn = Array.from(host.querySelectorAll('.pf-modal-cancel'))[0] as HTMLButtonElement
    act(() => { cancelBtn.dispatchEvent(new MouseEvent('click', { bubbles: true })) })

    // Exit check d: no request, modal unmounted, delete button still visible, session intact.
    expect(captured.url).toBeUndefined()   // fetch never called (stub throws if it were)
    expect(host.innerHTML).not.toContain('pf-modal')
    expect(host.querySelector('.pf-delete-btn')).toBeTruthy()
    expect(readSession()).toEqual(BASE_PROFILE)
    root.unmount()
    host.remove()
  })

  it('confirm happy path: DELETE /auth/me with credentials → guest landing + toast + session cleared (exit checks a + c)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    let calls = 0
    stubFetch(captured, () => {
      calls++
      return Promise.resolve(new Response(JSON.stringify({ userId: '1' }), { status: 200 }))
    })

    writeSession(BASE_PROFILE)
    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => {
      void root.render(createElement(App))
    })
    await act(async () => { navigateToProfile() })

    const deleteBtn = host.querySelector('.pf-delete-btn') as HTMLButtonElement
    act(() => { deleteBtn.dispatchEvent(new MouseEvent('click', { bubbles: true })) })

    const confirmBtn = Array.from(host.querySelectorAll('.pf-modal-confirm'))[0] as HTMLButtonElement
    await act(async () => {
      confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()   // DangerZone awaits the fetch…
      await Promise.resolve()   // …then App's synchronous clear + redirect block flushes.
    })

    // Exit check a (client half): exactly one DELETE /auth/me with credentials, no more.
    expect(calls).toBe(1)
    expect(captured.url).toContain('/auth/me')
    expect(captured.init?.method).toBe('DELETE')
    expect(captured.init?.credentials).toBe('include')
    // Exit check c: guest TopBar + landing toast + session gone.
    expect(host.innerHTML).toContain('<button type="button" class="tb-signin"')
    const statusDivs = Array.from(host.querySelectorAll('[role="status"]'))
    expect(statusDivs.some((d) => d.textContent === 'Your account was deleted.')).toBe(true)
    expect(readSession()).toBeNull()
    root.unmount()
    host.remove()
  })

  it('confirm → non-2xx: modal stays open with inline alert, session unchanged, no redirect (D-1 error path)', async () => {
    const captured: { url?: string; init?: RequestInit } = {}
    stubFetch(captured, () => {
      // T-008-06 problem shape (401 UNAUTHENTICATED — expired cookie; 404 covers the double-delete).
      return Promise.resolve(new Response(JSON.stringify({ title: 'UNAUTHENTICATED' }), { status: 401 }))
    })

    writeSession(BASE_PROFILE)
    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => {
      void root.render(createElement(App))
    })
    await act(async () => { navigateToProfile() })

    const deleteBtn = host.querySelector('.pf-delete-btn') as HTMLButtonElement
    act(() => { deleteBtn.dispatchEvent(new MouseEvent('click', { bubbles: true })) })

    const confirmBtn = Array.from(host.querySelectorAll('.pf-modal-confirm'))[0] as HTMLButtonElement
    await act(async () => {
      confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
      await Promise.resolve()
    })

    // Modal still open with the inline role="alert" error; account untouched; no redirect.
    expect(host.innerHTML).toContain('pf-modal-copy')
    const alertLines = Array.from(host.querySelectorAll('[role="alert"]'))
    expect(alertLines.some((p) => p.textContent === 'Could not delete your account. Please try again.')).toBe(true)
    expect(readSession()).toEqual(BASE_PROFILE)
    expect(window.location.pathname).toBe('/account/profile')
    root.unmount()
    host.remove()
  })

  it('GDPR copy pinned: the exact task-spec string with {display name} interpolated (exit check b)', async () => {
    const ada: StoredProfile = { ...BASE_PROFILE, displayName: 'Ada Lovelace' }
    writeSession(ada)
    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => {
      void root.render(createElement(App))
    })
    await act(async () => { navigateToProfile() })

    const deleteBtn = host.querySelector('.pf-delete-btn') as HTMLButtonElement
    act(() => { deleteBtn.dispatchEvent(new MouseEvent('click', { bubbles: true })) })

    // The pinned copy — verbatim, display name interpolated at render time (D-3).
    expect(host.querySelector('.pf-modal-copy')!.textContent)
      .toBe("Your account and profile are deleted. Your active transfers keep working and show 'From: Ada Lovelace' without your email.")
    root.unmount()
    host.remove()
  })

  it('US-047-02 (FR-047-7): the delete modal also tells the user their telemetry is anonymized within 24 hours', async () => {
    writeSession({ ...BASE_PROFILE, displayName: 'Ada Lovelace' })
    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => {
      void root.render(createElement(App))
    })
    await act(async () => { navigateToProfile() })

    const deleteBtn = host.querySelector('.pf-delete-btn') as HTMLButtonElement
    act(() => { deleteBtn.dispatchEvent(new MouseEvent('click', { bubbles: true })) })

    const copies = host.querySelectorAll('.pf-modal-copy')
    expect(copies).toHaveLength(2)
    expect(copies[1].textContent).toBe('Your telemetry data is anonymized within 24 hours of deletion.')
    root.unmount()
    host.remove()
  })

  describe('US-047-01: My data section behind feature.dataExport', () => {
    it('gate on: the My data section is rendered', async () => {
      featuresBody = { 'feature.dataExport': true }
      stubFetch({}, () => Promise.resolve(new Response(null, { status: 204 })))
      const { host, cleanup } = renderProfilePage(BASE_PROFILE)
      await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })
      expect(host.querySelector('.pf-export')).not.toBeNull()
      expect(host.textContent).toContain('My data')
      cleanup()
    })

    it('gate off (default): the My data section is absent', async () => {
      stubFetch({}, () => Promise.resolve(new Response(null, { status: 204 })))
      const { host, cleanup } = renderProfilePage(BASE_PROFILE)
      await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })
      expect(host.querySelector('.pf-export')).toBeNull()
      cleanup()
    })
  })

  describe('US-046-01: notification switch behind feature.emailSettings', () => {
    it('gate off (default): the notification switch is absent', async () => {
      stubFetch({}, () => Promise.resolve(new Response(null, { status: 204 })))
      const { host, cleanup } = renderProfilePage(BASE_PROFILE)
      await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })
      expect(host.querySelector('#pf-notify-switch')).toBeNull() // US-044-02 contract: absent unless the gate is on
      cleanup()
    })
  })

  describe('T-015-02 (F-TRF-015): theme select + live system OS tracking', () => {
    it("System mode follows the OS preference — resolved-set path, no removeAttribute fallback to :root light", async () => {
      mqStub.flipMatches(true)   // OS prefers dark.

      const captured: { url?: string; init?: RequestInit } = {}
      stubFetch(captured, () => Promise.resolve(new Response(JSON.stringify({ ...BASE_PROFILE, theme: 'system' }), { status: 200 })))

      const onSignedIn = vi.fn()
      const { host, cleanup } = renderProfilePage(BASE_PROFILE, onSignedIn)

      // Selecting 'system' must resolve to the OS preference — the old removeAttribute path fell back to :root light.
      const select = host.querySelector('#pf-theme') as HTMLSelectElement
      await act(async () => {
        setNativeSelectValue(select, 'system')
        await Promise.resolve()   // flush the PATCH .then (writeSession).
      })

      expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
      cleanup()
    })

    it("explicit choice does NOT follow OS changes — App's useSystemTheme registers no listener for explicit modes (EC-015-1)", async () => {
      mqStub.flipMatches(true)   // OS prefers dark.

      const captured: { url?: string; init?: RequestInit } = {}
      stubFetch(captured, () => Promise.resolve(new Response(JSON.stringify({ ...BASE_PROFILE, theme: 'light' }), { status: 200 })))

      const onSignedIn = vi.fn()
      const { host, cleanup } = renderProfilePage(BASE_PROFILE, onSignedIn)

      // Pick explicit light while the OS is dark — the resolved-set path must honor the account choice.
      const select = host.querySelector('#pf-theme') as HTMLSelectElement
      await act(async () => {
        setNativeSelectValue(select, 'light')
        await Promise.resolve()   // flush the PATCH .then (writeSession).
      })

      expect(document.documentElement.getAttribute('data-theme')).toBe('light')
      cleanup()
    })

    it("persistence: after changeTheme to Dark, readSession() returns theme='dark' — the mirror index.html's bootstrap reads (AC-015-1 client half)", async () => {
      const captured: { url?: string; init?: RequestInit } = {}
      stubFetch(captured, () => Promise.resolve(new Response(JSON.stringify({ ...BASE_PROFILE, theme: 'dark' }), { status: 200 })))

      writeSession(BASE_PROFILE)   // signed-in session — the mirror exists before the change.
      const onSignedIn = vi.fn()
      const { host, cleanup } = renderProfilePage(BASE_PROFILE, onSignedIn)

      const select = host.querySelector('#pf-theme') as HTMLSelectElement
      await act(async () => {
        setNativeSelectValue(select, 'dark')
        await Promise.resolve()   // flush the PATCH .then (writeSession).
      })

      expect(readSession()?.theme).toBe('dark')   // next paint's bootstrap resolves dark before first paint.
      cleanup()
    })
  })
})

describe('T-014-05 language select (AC-014-3)', () => {
  const KEY = 'protoDrop.accountLocale'

  afterEach(async () => {
    vi.restoreAllMocks()
    await act(async () => { await i18n.changeLanguage('en') })
  })

  it('renders 9 options, value empty without a key and nl when the key is pre-set', () => {
    const a = renderProfilePage(BASE_PROFILE)
    const sel = a.host.querySelector('#pf-language') as HTMLSelectElement
    expect(sel.options.length).toBe(9)
    expect(sel.options[0].value).toBe('')
    expect(sel.value).toBe('')
    a.cleanup()

    window.localStorage.setItem(KEY, 'nl')
    const b = renderProfilePage(BASE_PROFILE)
    expect((b.host.querySelector('#pf-language') as HTMLSelectElement).value).toBe('nl')
    b.cleanup()
  })

  it('selecting nl writes the key, calls changeLanguage and issues no request', async () => {
    stubFetch({}, () => { throw new Error('no requests allowed for a language change') })
    const spy = vi.spyOn(i18n, 'changeLanguage')
    const { host, cleanup } = renderProfilePage(BASE_PROFILE)
    await act(async () => { setNativeSelectValue(host.querySelector('#pf-language') as HTMLSelectElement, 'nl') })
    expect(window.localStorage.getItem(KEY)).toBe('nl')
    expect(spy).toHaveBeenCalledWith('nl')
    cleanup()
  })

  it('selecting Browser default removes the key and re-resolves the browser language', async () => {
    window.localStorage.setItem(KEY, 'nl')
    const spy = vi.spyOn(i18n, 'changeLanguage')
    const { host, cleanup } = renderProfilePage(BASE_PROFILE)
    await act(async () => { setNativeSelectValue(host.querySelector('#pf-language') as HTMLSelectElement, '') })
    expect(window.localStorage.getItem(KEY)).toBeNull()
    expect(spy).toHaveBeenCalledWith('en')
    cleanup()
  })

  it('full App: switching to nl re-renders the page in Dutch immediately', async () => {
    writeSession(BASE_PROFILE)
    const host = document.createElement('div')
    document.body.appendChild(host)
    const root = createRoot(host)
    act(() => { void root.render(createElement(App)) })
    await act(async () => {
      window.history.pushState('', '', '/account/profile')
      window.dispatchEvent(new PopStateEvent('popstate'))
    })
    expect(host.querySelector('label[for="pf-theme"]')?.textContent).toBe('Theme')
    await act(async () => { setNativeSelectValue(host.querySelector('#pf-language') as HTMLSelectElement, 'nl') })
    expect(host.querySelector('label[for="pf-theme"]')?.textContent).toBe('Thema')
    act(() => { root.unmount() })
    host.remove()
  })
})
