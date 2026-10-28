import { useState } from 'react'

import type { StoredProfile } from './auth/session'

interface TopBarProps {
  profile: StoredProfile | null
  onNavigateAccount: () => void
  onSignOut: () => void
}

/**
 * T-008-03 (UI-Reference §5.5): app-wide top bar at every route.
 *
 * Left wordmark "ProtoDrop" (--fs-small, weight 600). Right = guest "Sign in" button OR avatar circle
 * (initial of displayName/email, --accent-soft bg) with dropdown rows Profile / disabled theme select
 * (system/light/dark — live-wiring is T-008-07 via PATCH /auth/me) / Sign out.
 *
 * TopBar fires callbacks only; App owns route + session mutation (UnsubscribePage handleGo pattern).
 */
export function TopBar({ profile, onNavigateAccount, onSignOut }: TopBarProps) {
  const [menuOpen, setMenuOpen] = useState(false)

  const initial = (profile ? profile.displayName : '').trim().charAt(0) || (profile ? profile.email.charAt(0) : '')

  return (
    <header className="tb" aria-label="Top bar">
      <span className="tb-wordmark">ProtoDrop</span>
      {profile ? (
        <div className="tb-avatar-wrap">
          <button type="button" className="tb-avatar" onClick={() => setMenuOpen((v) => !v)} aria-haspopup="menu" aria-expanded={menuOpen}>
            {initial}
          </button>
          {menuOpen && (
            <div className="tb-dropdown" role="menu">
              <button type="button" className="tb-dropdown-row" role="menuitem">Profile</button>
              <select className="tb-theme-select" defaultValue={profile.theme} disabled aria-label="Theme" title="Theme selection ships in T-008-07">
                <option value="system">System</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </select>
              <button type="button" className="tb-dropdown-row" role="menuitem" onClick={onSignOut}>Sign out</button>
            </div>
          )}
        </div>
      ) : (
        <button type="button" className="tb-signin" onClick={onNavigateAccount}>Sign in</button>
      )}
    </header>
  )
}

export default TopBar
