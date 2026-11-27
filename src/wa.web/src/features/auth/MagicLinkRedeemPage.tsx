import { useEffect, useState } from 'react'

import { API_BASE } from '../../core/config/apiBase'
import type { StoredProfile } from '../../core/auth/session'
import { writeSession } from '../../core/auth/session'
import { getTraceParent } from '../../core/traceparent'
import i18n from '../../core/i18n/init'

interface MagicLinkRedeemPageProps {
  token: string
  onSignedIn: (p: StoredProfile) => void
}

type RedeemOutcome = { ok: true; profile: StoredProfile } | { ok: false; message: string }

// Single-use tokens: a second POST would 410 and overwrite the success. React StrictMode double-runs effects in dev,
// so the request is memoised per token for the page lifetime (one entry per token per load; never re-POSTed).
const inflight = new Map<string, Promise<RedeemOutcome>>()

async function redeem(token: string): Promise<RedeemOutcome> {
  try {
    const res = await fetch(`${API_BASE}/auth/magic-link/redeem`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
      // Cross-origin :5173 -> :8080 — the API's Set-Cookie (wa.session) is dropped without this.
      credentials: 'include',
      body: JSON.stringify({ token }),
    })
    if (res.ok) return { ok: true, profile: (await res.json()) as StoredProfile }

    // Non-2xx: Problem+JSON details verbatim (400 malformed, 410 LINK_EXPIRED) — same rule as AuthPage.
    let message = i18n.t('auth.error.generic')
    try {
      const body = (await res.json()) as { details?: string; title?: string }
      if (body.details) message = body.details
      else if (body.title) message = body.title
    } catch {
      // Non-JSON/unparseable body — keep the generic message.
    }
    return { ok: false, message }
  } catch {
    return { ok: false, message: i18n.t('auth.error.generic') }
  }
}

function redeemOnce(token: string): Promise<RedeemOutcome> {
  let p = inflight.get(token)
  if (!p) {
    p = redeem(token)
    inflight.set(token, p)
  }
  return p
}

/**
 * CHORE-magic-link-redeem (FR-008-1, EC-008-3): lands the emailed /auth?ml= link. Redeems the token once,
 * stores the session exactly like password login (writeSession + onSignedIn), and shows the server's error copy otherwise.
 */
export function MagicLinkRedeemPage({ token, onSignedIn }: MagicLinkRedeemPageProps) {
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    void redeemOnce(token).then((r) => {
      if (!active) return
      // Outcome consumed: never replay it (a re-mount for the same token must not re-sign-in).
      inflight.delete(token)
      if (r.ok) {
        writeSession(r.profile)
        // Replace (not push) the /auth?ml=<token> entry so Back can't land on the consumed link.
        window.history.replaceState(null, '', '/')
        onSignedIn(r.profile)
      } else {
        // Drop the token from the address bar; App's `search` state only refreshes on popstate, so this error UI stays mounted.
        window.history.replaceState(null, '', '/auth')
        setError(r.message)
      }
    })
    return () => { active = false }
  }, [token, onSignedIn])

  const goToAccount = (): void => {
    window.history.pushState('', '', '/account')
    window.dispatchEvent(new PopStateEvent('popstate'))
  }

  if (error === null) {
    return (
      <section className="au" aria-label={i18n.t('auth.page.ariaLabel')} aria-busy="true">
        <div className="au-card" />
      </section>
    )
  }

  return (
    <section className="au" aria-label={i18n.t('auth.page.ariaLabel')}>
      <div className="au-card">
        <p className="au-error" role="alert">{error}</p>
        <button type="button" className="au-ghost-btn" onClick={goToAccount}>
          {i18n.t('auth.ghost.magicLink')}
        </button>
      </div>
    </section>
  )
}

export default MagicLinkRedeemPage
