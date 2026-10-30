import { useState } from 'react'

import type { StoredProfile } from '../../core/auth/session'
import DangerZone from './DangerZone'

// T-008-06 wire (TA-4.2#12–13), verified against src/wa.api/Program.cs — GET/PATCH /auth/me + logout:
export const ME_BASE = 'http://localhost:8080/api/v1/auth/me'   // fixed-port absolute, AuthPage API_BASE precedent; shared with DangerZone (T-008-09 D-1)

interface ProfilePageProps {
  profile: StoredProfile                     // App owns the session; this is a prop (UnsubscribePage pattern)
  onSignedIn: (p: StoredProfile) => void      // App re-sets session state after a successful PATCH
  onThemeChange: (theme: 'system' | 'light' | 'dark') => void   // shared with TopBar — single source of truth in App
  onDeleteAccount: () => void                // T-008-09: App.handleDeleteAccount — fires after the 2xx DELETE (D-2).
}

/**
 * T-008-07 (UI-Reference §5.5): profile screen at /account/profile (signed-in only).
 * Display name input + Save (PATCH { displayName }), theme select (PATCH { theme } — server-wins, F-TRF-015),
 * read-only plan line, divider, danger-zone slot with the Delete account button + GDPR confirm modal (T-008-09).
 * First signed-in client fetch: credentials: 'include' carries the HttpOnly wa.session cookie cross-origin.
 * Sign out is via TopBar only (D-4), so no button here and no onSignOut prop (App wires it at App level, D-3/D-6).
 */
export function ProfilePage({ profile, onSignedIn, onThemeChange, onDeleteAccount }: ProfilePageProps) {
  const [name, setName] = useState(profile.displayName)
  const [theme, setThemeLocal] = useState<'system' | 'light' | 'dark'>(profile.theme)
  const [savingName, setSavingName] = useState(false)
  const [savingTheme, setSavingTheme] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // PATCH { displayName } only — theme is a separate field so the two Save paths stay independent.
  async function saveName() {
    setSavingName(true)
    setError(null)
    try {
      const res = await fetch(ME_BASE, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        // First signed-in client call — HttpOnly wa.session cookie must be sent cross-origin.
        credentials: 'include',
        body: JSON.stringify({ displayName: name.trim() })   // server trims + falls back to email local part on empty (T-008-06)
      })
      if (!res.ok) throw new Error((await res.json().catch(() => null))?.message ?? 'Could not save your display name.')
      const updated = (await res.json()) as Omit<StoredProfile, never>
      // Server already fell back for empty — show the SAVED value, never "(blank)" (exit check d).
      setName(updated.displayName)
      onSignedIn({ ...profile, displayName: updated.displayName })   // App re-sets session state; TopBar initial updates too.
    } catch {
      setError('Could not save your display name.')
    } finally {
      setSavingName(false)
    }
  }

  function changeTheme(next: 'system' | 'light' | 'dark') {
    // Apply the attribute pre-paint-style immediately so there's no flicker, then persist to the account.
    // F-TRF-015: guests fall back to prefers-color-scheme; a signed-in user's stored theme wins.
    if (next === 'system') document.documentElement.removeAttribute('data-theme')
    else document.documentElement.setAttribute('data-theme', next)

    setThemeLocal(next)
    onThemeChange(next)   // App updates its session state so TopBar's select stays in sync (D-4).

    setSavingTheme(true)
    setError(null)
    fetch(ME_BASE, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ theme: next })   // partial update — displayName untouched (T-008-06)
    })
      .then(async (res) => {
        if (!res.ok) throw new Error((await res.json().catch(() => null))?.message ?? 'Could not save your theme.')
        const updated = (await res.json()) as Omit<StoredProfile, never>
        onSignedIn({ ...profile, theme: updated.theme })   // server-wins; round-trip the stored value.
      })
      .catch(() => setError('Could not save your theme.'))
      .finally(() => setSavingTheme(false))
  }

  return (
    <div className="pf">
      <h1 className="pf-heading">Profile</h1>
      {/* Display name */}
      <label className="pf-label" htmlFor="pf-name">Display name</label>
      <input id="pf-name" className="pf-input" type="text" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
      <button type="button" className="pf-save-btn" disabled={savingName || name.trim() === profile.displayName} onClick={() => void saveName()}>
        Save
      </button>
      {/* Theme select — same three options as TopBar; value from the account (server-wins, F-TRF-015). */}
      <label className="pf-label" htmlFor="pf-theme">Theme</label>
      <select id="pf-theme" className="pf-theme-select" value={theme} disabled={savingTheme} onChange={(e) => changeTheme(e.target.value as 'system' | 'light' | 'dark')}>
        <option value="system">System</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
      {/* Plan line — read-only in MVP (FR-008-4: "Free by default"). planCode comes from the session DTO; no extra fetch. */}
      <p className="pf-plan">{profile.planCode === 'free' ? 'Free' : profile.planCode}</p>
      {/* Danger zone divider + slot — US-008-06's Delete account button + GDPR confirm modal (T-008-09 D-1/D-4). */}
      <hr className="pf-divider" />
      <div className="pf-danger-zone" aria-label="Danger zone">
        <DangerZone profile={profile} onDeleteAccount={onDeleteAccount} />
      </div>
      {error && <p role="alert" className="pf-error">{error}</p>}
    </div>
  )
}

export default ProfilePage
