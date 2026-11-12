import { useEffect, useRef, useState } from 'react'

import type { StoredProfile } from '../../core/auth/session'
import { writeSession } from '../../core/auth/session'
import { getTraceParent } from '../../core/traceparent'
import i18n from '../../core/i18n/init'

interface AuthPageProps {
  onSignedIn: (p: StoredProfile) => void
}

type Tab = 'sign-in' | 'sign-up'
type Phase = 'form' | 'link-sent'

// T-008-03: fixed local port, no proxy, no env vars yet (StagingList DRAFT_ENDPOINT precedent).
const API_BASE = 'http://localhost:8080/api/v1'

/**
 * T-008-03 (UI-Reference §5.5): auth screen at /account. Single card with copy-only Sign in / Sign up tabs
 * (FR-008-3 — same fields, same Continue button; headline + endpoint swap), inline email/password validation
 * BEFORE any fetch (FR-008-5), Continue → POST /auth/signup or /auth/login per tab, 2xx → writeSession(DTO) + onSignedIn
 * (App navigates home via pushState+popstate — AuthPage never calls location.assign), non-2xx Problem+JSON details
 * rendered verbatim in a form-level message region (no client code mapping; WRONG_PASSWORD already reads "Wrong password"
 * — no "user not found" anywhere). Ghost flows: "Email me a magic link" + "Forgot password?" → 10 s cooldown "Link sent to {email}" state,
 * form removed entirely ("no double-submission possible") (US-008-02/03).
 */
export function AuthPage({ onSignedIn }: AuthPageProps) {
  const [tab, setTab] = useState<Tab>('sign-in')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [formError, setFormError] = useState<string | null>(null) // form-level message (non-2xx details verbatim).
  const [phase, setPhase] = useState<Phase>('form')
  const [sentEmail, setSentEmail] = useState('')
  const [forgotActive, setForgotActive] = useState(false) // email-only sub-phase hides password field, renames Continue → "Send link".
  const [cooldownRemaining, setCooldownRemaining] = useState<number | null>(null)
  const cooldownRef = useRef<number | null>(null)

  // 1 s setInterval decrement; at 0 → null re-enables. Uses a ref so the interval is managed imperatively.
  const startCooldown = (): void => {
    setCooldownRemaining(10)
    if (cooldownRef.current !== null) window.clearInterval(cooldownRef.current)
    cooldownRef.current = window.setInterval(() => {
      setCooldownRemaining((prev) => {
        if (prev === null) return null
        const next = prev - 1
        return next > 0 ? next : null // re-enable at the 10th tick boundary.
      })
    }, 1000)
  }

  // Only clean up the interval on unmount, not when phase changes.
  useEffect(() => {
    return () => {
      if (cooldownRef.current !== null) {
        window.clearInterval(cooldownRef.current)
      }
    }
  }, [])

  const isValidEmail = (value: string): boolean => {
    const trimmed = value.trim()
    return trimmed.length > 0 && trimmed.includes('@')
  }

  const isShortPassword = (value: string): boolean => value.length < 8 // FR-008-5.

  const handleContinue = async (): Promise<void> => {
    setFormError(null)
    if (!isValidEmail(email)) {
      setFormError(i18n.t('auth.error.invalidEmail'))
      return
    }
    if (!forgotActive && isShortPassword(password)) {
      setFormError(i18n.t('auth.error.shortPassword'))
      return
    }

    const url = forgotActive
      ? `${API_BASE}/auth/forgot-password`
      : tab === 'sign-up'
        ? `${API_BASE}/auth/signup`
        : `${API_BASE}/auth/login`

    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
        // Cross-origin :5173 → :8080 — the API's Set-Cookie (wa.session) is dropped without this.
        credentials: 'include',
        body: JSON.stringify(forgotActive ? { email } : { email, password }),
      })
      if (res.ok) {
        if (forgotActive) {
          // Email-only flow → "Link sent to {email}" state, form removed entirely.
          setSentEmail(email)
          setPhase('link-sent')
          startCooldown()
          return
        }
        const dto = (await res.json()) as StoredProfile
        writeSession(dto) // client persistence (T-008-03 NOTES).
        onSignedIn(dto) // App navigates home via pushState+popstate.
        return
      }

      // Non-2xx: render Problem+JSON details verbatim in form-level region.
      let detail = i18n.t('auth.error.generic')
      try {
        const body = (await res.json()) as { details?: string; title?: string }
        if (body.details) detail = body.details
        else if (body.title) detail = body.title
      } catch {
        // Defensive fallback for non-JSON/unparseable responses.
      }
      setFormError(detail)
    } catch {
      setFormError(i18n.t('auth.error.generic'))
    }
  }

  const handleGhostLink = async (): Promise<void> => {
    setFormError(null)
    if (!isValidEmail(email)) {
      setFormError(i18n.t('auth.error.invalidEmail'))
      return
    }
    // Magic link ghost: POST to /auth/magic-link (email only, no password).
    try {
      const res = await fetch(`${API_BASE}/auth/magic-link`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
        body: JSON.stringify({ email }),
      })
      if (res.ok) {
        setSentEmail(email)
        setPhase('link-sent')
        startCooldown()
        return
      }
      let detail = i18n.t('auth.error.generic')
      try {
        const body = (await res.json()) as { details?: string; title?: string }
        if (body.details) detail = body.details
        else if (body.title) detail = body.title
      } catch {
        // Defensive fallback.
      }
      setFormError(detail)
    } catch {
      setFormError(i18n.t('auth.error.generic'))
    }
  }

  const handleForgotPassword = (): void => {
    setFormError(null)
    setForgotActive(true)
  }

  if (phase === 'link-sent') {
    // "Link sent to {email}" state — form removed entirely (no double-submission possible).
    return (
      <section className="au" aria-label={i18n.t('auth.linkSent.sectionAria')}>
        <div className="au-card">
          <h1 className="au-heading">{i18n.t('auth.linkSent.heading', { email: sentEmail })}</h1>
          <button type="button" className="au-ghost-btn" disabled={cooldownRemaining !== null}>
            {i18n.t('auth.resend.label')}{cooldownRemaining !== null ? ` (${cooldownRemaining})` : ''}
          </button>
        </div>
      </section>
    )
  }

  return (
    <section className="au" aria-label={i18n.t('auth.page.ariaLabel')}>
      <div className="au-card">
        {/* Copy-only tabs — same fields, same button per FR-008-3 / EC-008-4. */}
        <div className="au-tabs">
          <button
            type="button"
            className={`au-tab${tab === 'sign-in' ? ' au-tab--active' : ''}`}
            onClick={() => setTab('sign-in')}
            aria-selected={tab === 'sign-in'}
            role="tab"
          >
            {i18n.t('auth.signIn.tab')}
          </button>
          <button
            type="button"
            className={`au-tab${tab === 'sign-up' ? ' au-tab--active' : ''}`}
            onClick={() => setTab('sign-up')}
            aria-selected={tab === 'sign-up'}
            role="tab"
          >
            {i18n.t('auth.signUp.tab')}
          </button>
        </div>

        <h1 className="au-heading">{tab === 'sign-in' ? i18n.t('auth.heading.signIn') : i18n.t('auth.heading.createAccount')}</h1>

        <label className="au-label" htmlFor="auth-email">{i18n.t('auth.field.emailLabel')}</label>
        <input
          id="auth-email"
          className="au-input"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
        />

        {!forgotActive && (
          <>
            <label className="au-label" htmlFor="auth-password">{i18n.t('auth.field.passwordLabel')}</label>
            <input
              id="auth-password"
              className="au-input"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete={tab === 'sign-up' ? 'new-password' : 'current-password'}
            />
            <p className="au-helper">{i18n.t('auth.helper.minChars')}</p>
          </>
        )}

        {formError && (
          <p className="au-error" role="alert">{formError}</p>
        )}

        <button type="button" className="au-continue-btn" onClick={() => void handleContinue()}>
          {forgotActive ? i18n.t('auth.button.sendLink') : i18n.t('auth.button.continue')}
        </button>

        {!forgotActive && (
          <button type="button" className="au-ghost-btn" onClick={() => void handleGhostLink()}>
            {i18n.t('auth.ghost.magicLink')}
          </button>
        )}

        {!forgotActive && (
          <button type="button" className="au-ghost-btn" onClick={handleForgotPassword}>
            {i18n.t('auth.ghost.forgotPassword')}
          </button>
        )}
      </div>
    </section>
  )
}

export default AuthPage
