import { useEffect, useState } from 'react'

interface UnsubscribePageProps {
  token: string
}

/**
 * T-006-03 (FR-006-5, UI-Reference §4.6): unsubscribe landing page at /unsubscribe/{token}.
 * Centered card with "You are on the list no more." + Go to ProtoDrop button.
 * Calls GET /unsubscribe/{token} on mount; success or 404 both show the same card (don't leak detail).
 * Button navigates to / via pushState + popstate (same pattern as RecipientPage's handleGrowthCtaClick).
 */
export function UnsubscribePage({ token }: UnsubscribePageProps) {
  const [done, setDone] = useState(false)

  useEffect(() => {
    let cancelled = false
    fetch(`/unsubscribe/${token}`)
      .then(() => { if (!cancelled) setDone(true) })
      .catch(() => { if (!cancelled) setDone(true) }) // 404 or network error → still show the card (no leak)
    return () => { cancelled = true }
  }, [token])

  const handleGo = () => {
    window.history.pushState('', '', '/')
    window.dispatchEvent(new PopStateEvent('popstate'))
  }

  if (!done) {
    return <p className="uns-loading" role="status">Processing…</p>
  }

  return (
    <section className="uns uns-card" aria-label="Unsubscribed">
      <h1 className="uns-heading">You are on the list no more.</h1>
      <button type="button" className="btn-primary uns-action" onClick={handleGo}>Go to ProtoDrop</button>
    </section>
  )
}

export default UnsubscribePage
