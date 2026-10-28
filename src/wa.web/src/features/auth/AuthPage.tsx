import { useEffect, useRef, useState } from 'react'

import type { StoredProfile } from '../../core/auth/session'
import { writeSession } from '../../core/auth/session'

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
      setFormError('Enter your email address')
      return
    }
    if (!forgotActive && isShortPassword(password)) {
      setFormError('Password must be at least 8 characters')
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
        headers: { 'Content-Type': 'application/json' },
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
      let detail = 'Something went wrong. Please try again.'
      try {
        const body = (await res.json()) as { details?: string; title?: string }
        if (body.details) detail = body.details
        else if (body.title) detail = body.title
      } catch {
        // Defensive fallback for non-JSON/unparseable responses.
      }
      setFormError(detail)
    } catch {
      setFormError('Something went wrong. Please try again.')
    }
  }

  const handleGhostLink = async (): Promise<void> => {
    setFormError(null)
    if (!isValidEmail(email)) {
      setFormError('Enter your email address')
      return
    }
    // Magic link ghost: POST to /auth/magic-link (email only, no password).
    try {
      const res = await fetch(`${API_BASE}/auth/magic-link`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      })
      if (res.ok) {
        setSentEmail(email)
        setPhase('link-sent')
        startCooldown()
        return
      }
      let detail = 'Something went wrong. Please try again.'
      try {
        const body = (await res.json()) as { details?: string; title?: string }
        if (body.details) detail = body.details
        else if (body.title) detail = body.title
      } catch {
        // Defensive fallback.
      }
      setFormError(detail)
    } catch {
      setFormError('Something went wrong. Please try again.')
    }
  }

  const handleForgotPassword = (): void => {
    setFormError(null)
    setForgotActive(true)
  }

  if (phase === 'link-sent') {
    // "Link sent to {email}" state — form removed entirely (no double-submission possible).
    return (
      <section className="au" aria-label="Link sent">
        <div className="au-card">
          <h1 className="au-heading">Link sent to {sentEmail}</h1>
          <button type="button" className="au-ghost-btn" disabled={cooldownRemaining !== null}>
            Didn't get it? Send another{cooldownRemaining !== null ? ` (${cooldownRemaining})` : ''}
          </button>
        </div>
      </section>
    )
  }

  return (
    <section className="au" aria-label="Auth">
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
            Sign in
          </button>
          <button
            type="button"
            className={`au-tab${tab === 'sign-up' ? ' au-tab--active' : ''}`}
            onClick={() => setTab('sign-up')}
            aria-selected={tab === 'sign-up'}
            role="tab"
          >
            Sign up
          </button>
        </div>

        <h1 className="au-heading">{tab === 'sign-in' ? 'Sign in' : 'Create your account'}</h1>

        <label className="au-label" htmlFor="auth-email">Email</label>
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
            <label className="au-label" htmlFor="auth-password">Password</label>
            <input
              id="auth-password"
              className="au-input"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete={tab === 'sign-up' ? 'new-password' : 'current-password'}
            />
            <p className="au-helper">At least 8 characters.</p>
          </>
        )}

        {formError && (
          <p className="au-error" role="alert">{formError}</p>
        )}

        <button type="button" className="au-continue-btn" onClick={() => void handleContinue()}>
          {forgotActive ? 'Send link' : 'Continue'}
        </button>

        {!forgotActive && (
          <button type="button" className="au-ghost-btn" onClick={() => void handleGhostLink()}>
            Email me a magic link
          </button>
        )}

        {!forgotActive && (
          <button type="button" className="au-ghost-btn" onClick={handleForgotPassword}>
            Forgot password?
          </button>
        )}
      </div>
    </section>
  )
}

export default AuthPage
