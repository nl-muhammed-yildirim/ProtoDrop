import { useEffect, useState } from 'react'

import TopBar from './core/TopBar'
import type { StoredProfile } from './core/auth/session'
import { clearSession, readSession } from './core/auth/session'
import { useUploadEngine } from './core/upload/UploadEngine'
import DropZone from './features/landing/DropZone'
import StagingList from './features/landing/StagingList'
import AuthPage from './features/auth/AuthPage'
import { matchAccountPath } from './features/auth/authRoute'
import ProfilePage from './features/profile/ProfilePage'
import { matchProfilePath, PROFILE_PATH } from './features/profile/profileRoute'
import RecipientPage from './features/recipient/RecipientPage'
import UnsubscribePage from './features/unsubscribe/UnsubscribePage'
import { matchLinkPath } from './features/recipient/recipientRoute'
import { matchUnsubscribePath } from './features/unsubscribe/unsubscribeRoute'

/**
 * T-003-02 (FR-003-1): pathname routing — /t/{linkId} → recipient page; landing unchanged at /.
 * T-008-03: TopBar above <main> on every route (guest "Sign in" / signed-in avatar); /account/* → AuthPage.
 * Read-once + popstate refresh: the app has no router yet, so this is the minimal change that keeps
 * pushState (tests) and browser back/forward both working. The typeof guard keeps the SSR test
 * (App.test.tsx) hermetic — it renders App in node where window does not exist.
 */
export default function App() {
  const [pathname, setPathname] = useState(() => (typeof window === 'undefined' ? '/' : window.location.pathname))
  // T-008-03: lazy-init from localStorage; the typeof-window guard inside readSession keeps SSR null.
  const [session, setSession] = useState<StoredProfile | null>(() => readSession())

  useEffect(() => {
    const onPop = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  const linkId = matchLinkPath(pathname)
  const unsubscribeToken = matchUnsubscribePath(pathname)
  const hasFiles = useUploadEngine((s) => s.files.length > 0)

  // T-008-09 (D-5): post-delete toast — App state survives the ProfilePage unmount on redirect; DropZone renders it.
  const [deleteToast, setDeleteToast] = useState<string | null>(null)

  useEffect(() => {
    if (!deleteToast) return
    const t = window.setTimeout(() => setDeleteToast(null), 5000)   // DropZone toast precedent: 5 s auto-dismiss
    return () => window.clearTimeout(t)
  }, [deleteToast])

  const goHome = () => {
    window.history.pushState('', '', '/')
    window.dispatchEvent(new PopStateEvent('popstate')) // existing popstate listener updates pathname.
  }

  const handleSignOut = () => {
    // T-008-07: POST /auth/logout clears the server-side wa.session cookie (T-008-06, TA-4.2#13).
    // Fire-and-forget with a local fallback — even if the network fails we still clear the client session
    // so the TopBar returns to "Sign in" (exit check c); the stale server-side cookie will 401 on next GET /auth/me.
    fetch('http://localhost:8080/api/v1/auth/logout', { method: 'POST', credentials: 'include' }).catch(() => {})
    clearSession()
    setSession(null)
    goHome()
  }

  const handleDeleteAccount = () => {
    // T-008-09 (FR-008-8): DELETE already returned 2xx — account is gone server-side, cookie cleared by the API.
    // Order matters (unlike sign-out's fire-and-forget D-3 of T-008-07): the toast claims success, so it only
    // fires on a confirmed 2xx; local state then clears unconditionally — a failed server outcome is impossible here.
    clearSession()
    setSession(null)
    setDeleteToast('Your account was deleted.')
    goHome()   // pushState / + popstate dispatch (existing App helper) → ProfilePage unmounts, landing renders.
  }

  const handleThemeChange = (theme: 'system' | 'light' | 'dark') => {
    // Single source of truth for theme across TopBar + ProfilePage. The PATCH itself is owned by the
    // component that initiated it (ProfilePage.changeTheme or — in this task's scope — nothing else,
    // because TopBar's select is wired to the same onThemeChange but its own PATCH lives in D-4).
    setSession((s) => (s ? { ...s, theme } : s))
  }

  return (
    <>
      <TopBar
        profile={session}
        onNavigateAccount={() => {
          window.history.pushState('', '', session ? PROFILE_PATH : '/account')
          window.dispatchEvent(new PopStateEvent('popstate'))
        }}
        onThemeChange={handleThemeChange}
        onSignOut={handleSignOut}
      />
      <main>
        {unsubscribeToken !== null ? (
          <UnsubscribePage token={unsubscribeToken} />
        ) : session && matchProfilePath(pathname) ? (
          <ProfilePage profile={session} onSignedIn={setSession} onThemeChange={handleThemeChange} onDeleteAccount={handleDeleteAccount} />
        ) : matchAccountPath(pathname) ? (
          // Signed-out guest keeps the sign-in card; /account/profile with no session falls
          // through here too (it still matches ACCOUNT_PATH_RE — order above is what wins).
          <AuthPage onSignedIn={(p) => { setSession(p); goHome() }} />
        ) : linkId !== null ? (
          <RecipientPage linkId={linkId} />
        ) : (
          <>
            <DropZone toast={deleteToast} />
            {hasFiles && <StagingList />}
          </>
        )}
      </main>
    </>
  )
}
