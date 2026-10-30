// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AuthPage from './AuthPage'
import type { StoredProfile } from '../../core/auth/session'
import { clearSession } from '../../core/auth/session'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// Client mount (not renderToStaticMarkup): the form is stateful with POSTs — SSR would capture the pre-submit state.
function renderAuth(onSignedIn?: (p: StoredProfile) => void): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(
      createElement(AuthPage, { onSignedIn: onSignedIn ?? (() => {}) }),
    )
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

function stubFetch(responder?: (req: Request) => Response | Promise<Response>): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
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

beforeEach(() => {
  clearSession()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('AuthPage', () => {
  it('sign-up tab + valid email+password posts to /auth/signup and calls onSignedIn with the returned profile DTO (AC-008-1 client half)', async () => {
    const dto: StoredProfile = { userId: 'u1', email: 'a@b.c', displayName: 'A', theme: 'system', planCode: 'free' }
    let capturedUrl = ''
    stubFetch((req) => {
      capturedUrl = req.url
      return Promise.resolve(
        new Response(JSON.stringify(dto), { status: 200 }),
      )
    })

    const onSignedIn = vi.fn()
    const { host, cleanup } = renderAuth(onSignedIn)

    // Switch to sign-up tab.
    const tabs = Array.from(host.querySelectorAll('.au-tab')) as HTMLButtonElement[]
    act(() => {
      tabs[1]!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })
    expect(host.innerHTML).toContain('>Create your account</h1>')

    // Fill email + password.
    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    const pwInput = host.querySelector('#auth-password') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')
    setNativeValue(pwInput, 'password123')

    // Click Continue.
    const continueBtn = Array.from(host.querySelectorAll('.au-continue-btn'))[0] as HTMLButtonElement
    await act(async () => {
      continueBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    cleanup()
    expect(capturedUrl).toContain('/auth/signup')
    expect(onSignedIn).toHaveBeenCalledTimes(1)
    expect(onSignedIn).toHaveBeenCalledWith(dto)
  })

  it('sign-in tab posts to /auth/login with the same form and button (EC-008-4)', async () => {
    let capturedUrl = ''
    stubFetch((req) => {
      capturedUrl = req.url
      return Promise.resolve(new Response(null, { status: 200 }))
    })

    const { host, cleanup } = renderAuth()

    // Fill email + password on sign-in tab (default).
    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    const pwInput = host.querySelector('#auth-password') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')
    setNativeValue(pwInput, 'password123')

    // Click Continue.
    const continueBtn = Array.from(host.querySelectorAll('.au-continue-btn'))[0] as HTMLButtonElement
    await act(async () => {
      continueBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    cleanup()
    expect(capturedUrl).toContain('/auth/login')
  })

  it('wrong password shows "Wrong password" inline and never "user not found" (exit check 2)', async () => {
    stubFetch(() => {
      return Promise.resolve(
        new Response(
          JSON.stringify({ code: 'WRONG_PASSWORD', details: 'Wrong password' }),
          { status: 401 },
        ),
      )
    })

    const { host, cleanup } = renderAuth()

    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    const pwInput = host.querySelector('#auth-password') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')
    setNativeValue(pwInput, 'wrongpass123')

    const continueBtn = Array.from(host.querySelectorAll('.au-continue-btn'))[0] as HTMLButtonElement
    await act(async () => {
      continueBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    const html = host.innerHTML
    expect(html).toContain('Wrong password') // inline from Problem+JSON.
    cleanup()
    // No "user not found" anywhere in the DOM.
    expect(html).not.toMatch(/user not found/i)
  })

  it('password under 8 chars fails inline before any fetch (FR-008-5)', () => {
    let fetchCalled = false
    stubFetch(() => {
      fetchCalled = true
      return Promise.reject(new Error('should not be called'))
    })

    const { host, cleanup } = renderAuth()

    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    const pwInput = host.querySelector('#auth-password') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')
    setNativeValue(pwInput, 'short') // < 8 chars.

    const continueBtn = Array.from(host.querySelectorAll('.au-continue-btn'))[0] as HTMLButtonElement
    act(() => {
      continueBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    expect(host.innerHTML).toContain('Password must be at least 8 characters')
    expect(fetchCalled).toBe(false)
    cleanup()
  })

  it('magic-link ghost posts to /auth/magic-link and replaces the form with "Link sent to {email}" (exit check 4)', async () => {
    let capturedUrl = ''
    stubFetch((req) => {
      capturedUrl = req.url
      return Promise.resolve(
        new Response(null, { status: 200 }),
      )
    })

    const { host, cleanup } = renderAuth()

    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')

    // Click "Email me a magic link" ghost.
    const ghosts = Array.from(host.querySelectorAll('.au-ghost-btn')) as HTMLButtonElement[]
    const magicLinkBtn = ghosts.find((g) => g.textContent === 'Email me a magic link')!
    await act(async () => {
      magicLinkBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    const html = host.innerHTML
    expect(html).toContain('Link sent to a@b.c') // email in the state.
    // Password input gone from DOM (form removed entirely).
    expect(html).not.toContain('#auth-password')
    // Resend button disabled initially.
    expect(html).toContain('disabled=""')
    expect(capturedUrl).toContain('/auth/magic-link')
    cleanup()
  })

  it('cooldown disables re-send for 10 s then re-enables (exit check 4)', async () => {
    vi.useFakeTimers()
    stubFetch(() => Promise.resolve(new Response(null, { status: 200 })))

    const { host, cleanup } = renderAuth()

    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')

    const ghosts = Array.from(host.querySelectorAll('.au-ghost-btn')) as HTMLButtonElement[]
    const magicLinkBtn = ghosts.find((g) => g.textContent === 'Email me a magic link')!
    await act(async () => {
      magicLinkBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    // Advance 9 s (wrapped in act so React flushes each tick's setState) → still disabled.
    for (let i = 0; i < 9; i++) {
      act(() => {
        vi.advanceTimersByTime(1000)
      })
    }
    let resendBtn = Array.from(host.querySelectorAll('.au-ghost-btn'))[0] as HTMLButtonElement
    expect(resendBtn.disabled).toBe(true)

    // Advance 1 more s (10 s total) → enabled, label "Didn't get it? Send another".
    act(() => {
      vi.advanceTimersByTime(1000)
    })
    resendBtn = Array.from(host.querySelectorAll('.au-ghost-btn'))[0] as HTMLButtonElement
    expect(resendBtn.disabled).toBe(false)
    expect(resendBtn.textContent).toContain('Didn\'t get it? Send another')

    cleanup()
  })

  it('forgot-password: email-only step posts to /auth/forgot-password and shows the identical link-sent state (exit check 5)', async () => {
    let capturedUrl = ''
    stubFetch((req) => {
      capturedUrl = req.url
      return Promise.resolve(
        new Response(null, { status: 200 }),
      )
    })

    const { host, cleanup } = renderAuth()

    // Fill email only.
    const emailInput = host.querySelector('#auth-email') as HTMLInputElement
    setNativeValue(emailInput, 'a@b.c')

    // Click "Forgot password?" ghost.
    const ghosts = Array.from(host.querySelectorAll('.au-ghost-btn')) as HTMLButtonElement[]
    const forgotBtn = ghosts.find((g) => g.textContent === 'Forgot password?')!
    act(() => {
      forgotBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    // Password field hidden now.
    expect(host.innerHTML).not.toContain('#auth-password')

    // Continue (now "Send link").
    const continueBtn = Array.from(host.querySelectorAll('.au-continue-btn'))[0] as HTMLButtonElement
    await act(async () => {
      continueBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await Promise.resolve()
    })

    const html = host.innerHTML
    expect(html).toContain('Link sent to a@b.c') // identical link-sent state.
    expect(capturedUrl).toContain('/auth/forgot-password')
    cleanup()
  })
})
