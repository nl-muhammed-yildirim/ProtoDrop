import { useState } from 'react'

import type { StoredProfile } from './auth/session'

interface TopBarProps {
  profile: StoredProfile | null
  onNavigateAccount: () => void
  onThemeChange: (t: 'system' | 'light' | 'dark') => void
  onSignOut: () => void
}

/**
 * T-008-03 (UI-Reference §5.5): app-wide top bar at every route.
 *
 * Left wordmark "ProtoDrop" (--fs-small, weight 600). Right = guest "Sign in" button OR avatar circle
 * (initial of displayName/email, --accent-soft bg) with dropdown rows Profile / theme select
 * (system/light/dark — T-008-07 live-wired via PATCH /auth/me) / Sign out.
 *
 * TopBar fires callbacks only; App owns route + session mutation (UnsubscribePage handleGo pattern).
 */
export function TopBar({ profile, onNavigateAccount, onThemeChange, onSignOut }: TopBarProps) {
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
              <button type="button" className="tb-dropdown-row" role="menuitem" onClick={() => onNavigateAccount()}>Profile</button>
              <select className="tb-theme-select" value={profile.theme} aria-label="Theme" onChange={(e) => onThemeChange(e.target.value as 'system' | 'light' | 'dark')} onClick={() => setMenuOpen(false)}>
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
