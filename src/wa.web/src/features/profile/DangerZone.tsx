import { useEffect, useState } from 'react'

import type { StoredProfile } from '../../core/auth/session'
import { ME_BASE } from './ProfilePage'

interface DangerZoneProps {
  profile: StoredProfile           // display name feeds the modal copy interpolation only
  onDeleteAccount: () => void      // App: clearSession + setSession(null) + toast + goHome (D-2/D-5)
}

/**
 * T-008-09 (FR-008-8, UI-Reference §5.5 danger zone): red-tinted Delete account button + GDPR
 * confirm modal in the profile's reserved slot. No request is sent until Confirm — Cancel just
 * closes (exit check d). On 2xx fires onDeleteAccount exactly once; on failure keeps the modal
 * open with an inline alert so the user can retry or cancel.
 */
export function DangerZone({ profile, onDeleteAccount }: DangerZoneProps) {
  const [open, setOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Escape closes the modal (= cancel; no request has been sent or is in flight on close-after-failure).
  useEffect(() => {
    if (!open) return
    function onKey(e: KeyboardEvent): void { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey) }
  }, [open])

  async function close(): Promise<void> {
    setOpen(false)
    setError(null)   // a fresh open must not show the previous failure
  }

  async function confirm() {
    setDeleting(true)
    setError(null)
    try {
      const res = await fetch(ME_BASE, { method: 'DELETE', credentials: 'include' })
      if (!res.ok) throw new Error('Could not delete your account. Please try again.')
      onDeleteAccount()   // App clears session + navigates to / — this component unmounts here; no state after.
    } catch {
      setError('Could not delete your account. Please try again.')
    } finally {
      setDeleting(false)  // only reachable when the modal is still mounted (the error path).
    }
  }

  return (
    <>
      <button type="button" className="pf-delete-btn" onClick={() => setOpen(true)}>
        Delete account
      </button>
      {open && (
        <div className="pf-modal-backdrop" onClick={close} aria-hidden="true">
          <div className="pf-modal" role="dialog" aria-modal="true" aria-labelledby="pf-delete-title" onClick={(e) => e.stopPropagation()}>
            <h2 className="pf-modal-title" id="pf-delete-title">Delete your account?</h2>
            {/* Pinned GDPR copy (task spec instructions §2) — {display name} interpolates profile.displayName. */}
            <p className="pf-modal-copy">
              Your account and profile are deleted. Your active transfers keep working and show 'From: {profile.displayName}' without your email.
            </p>
            {error && <p role="alert" className="pf-modal-error">{error}</p>}
            <div className="pf-modal-actions">
              <button type="button" className="pf-modal-cancel" disabled={deleting} onClick={() => void close()}>Cancel</button>
              <button type="button" className="pf-modal-confirm" autoFocus disabled={deleting} onClick={() => void confirm()} aria-busy={deleting}>
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

export default DangerZone
