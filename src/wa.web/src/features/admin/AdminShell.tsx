import { useEffect, useState } from 'react'

import AdminFlagsPage from './AdminFlagsPage'
import AdminOverviewPage from './AdminOverviewPage'
import AdminSuppressionsPage from './AdminSuppressionsPage'
import AdminTransfersPage from './AdminTransfersPage'
import AdminUsersPage from './AdminUsersPage'
import { ADMIN_NAV_ITEMS } from './adminRoute'
import type { AdminScreen } from './adminRoute'

interface AdminShellProps {
  screen: AdminScreen
}

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AuthPage + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

type RoleState =
  | { kind: 'loading' }
  | { kind: 'signed-in'; role: 'SuperAdmin' | 'Operator' }
  | { kind: 'not-signed-in' }   // 401 UNAUTHENTICATED or 404 NOT_FOUND (valid cookie, no user row)
  | { kind: 'forbidden' }       // problem code FORBIDDEN — non-admin

/**
 * T-011-10 (FR-011-2): admin layout — left nav with all five items + role badge from /auth/me.
 * Role comes ONLY from GET /auth/me (T-011-01 gate) — the client session has NO role field.
 * Non-admins hitting /admin/* land on "Not an admin" instead of breaking the account fallback chain.
 */
export function AdminShell({ screen }: AdminShellProps) {
  const [roleState, setRoleState] = useState<RoleState>({ kind: 'loading' })

  useEffect(() => {
    const loadRole = async () => {
      try {
        const res = await fetch(`${API_BASE}/auth/me`, { credentials: 'include' })
        if (res.ok) {
          const body = (await res.json()) as { role?: string }
          if (body.role === 'superadmin') setRoleState({ kind: 'signed-in', role: 'SuperAdmin' })
          else if (body.role === 'operator') setRoleState({ kind: 'signed-in', role: 'Operator' })
          else setRoleState({ kind: 'forbidden' }) // role key absent — non-admin
          return
        }
        // Problem+JSON body: 401 UNAUTHENTICATED / 404 NOT_FOUND → sign-in prompt; FORBIDDEN → not an admin.
        let code = ''
        try {
          const body = (await res.json()) as { code?: string }
          if (body.code) code = body.code
        } catch {
          // Defensive fallback for non-JSON responses.
        }
        if (code === 'FORBIDDEN') setRoleState({ kind: 'forbidden' })
        else setRoleState({ kind: 'not-signed-in' })
      } catch {
        setRoleState({ kind: 'not-signed-in' })
      }
    }
    void loadRole()
  }, [])

  const navigate = (path: string) => {
    window.history.pushState('', '', path)
    window.dispatchEvent(new PopStateEvent('popstate')) // App's popstate listener updates pathname.
  }

  if (roleState.kind === 'loading') {
    return <div className="ad-loading">Loading admin…</div>
  }

  if (roleState.kind === 'not-signed-in') {
    return (
      <section className="ad-prompt" aria-label="Sign in required">
        <h1 className="ad-prompt-h1">Sign in to continue</h1>
        <p className="ad-prompt-text">Your session has expired. Please sign in again to access the admin console.</p>
        <button type="button" className="au-ghost-btn" onClick={() => navigate('/account')}>
          Go to sign in
        </button>
      </section>
    )
  }

  if (roleState.kind === 'forbidden') {
    return (
      <section className="ad-prompt" aria-label="Not an admin">
        <h1 className="ad-prompt-h1">Not an admin</h1>
        <p className="ad-prompt-text">This area is restricted to admin accounts.</p>
      </section>
    )
  }

  const activeItem = ADMIN_NAV_ITEMS.find((i) => i.path === window.location.pathname) ?? null

  return (
    <div className="ad-layout">
      <nav className="ad-nav" aria-label="Admin navigation">
        {ADMIN_NAV_ITEMS.map((item) => (
          <button
            key={item.path}
            type="button"
            className={`ad-nav-item${activeItem?.path === item.path ? ' ad-nav-active' : ''}`}
            onClick={() => navigate(item.path)}
          >
            {item.label}
          </button>
        ))}
      </nav>
      <div className="ad-content">
        <div className="ad-header">
          <span className="ad-badge">{roleState.role}</span>
        </div>
        {screen === 'overview' ? (
          <AdminOverviewPage />
        ) : screen === 'transfers' ? (
          <AdminTransfersPage role={roleState.role} />
        ) : screen === 'users' ? (
          <AdminUsersPage role={roleState.role} />
        ) : screen === 'flags' ? (
          <AdminFlagsPage role={roleState.role} />
        ) : screen === 'suppressions' ? (
          <AdminSuppressionsPage role={roleState.role} />
        ) : (
          <div className="ad-placeholder">
            <h1 className="mf-h1">{ADMIN_NAV_ITEMS.find((i) => i.label === screenLabel(screen))?.label ?? screen} — coming soon</h1>
          </div>
        )}
      </div>
    </div>
  )
}

function screenLabel(screen: AdminScreen): string {
  switch (screen) {
    case 'transfers': return 'Transfers'
    case 'users': return 'Users'
    case 'flags': return 'Flags'
    case 'suppressions': return 'Suppressions'
    default: return 'Overview'
  }
}

export default AdminShell
