import { useEffect, useState } from 'react'

import TopBar from './core/TopBar'
import type { StoredProfile } from './core/auth/session'
import { clearSession, readSession } from './core/auth/session'
import { useUploadEngine } from './core/upload/UploadEngine'
import DropZone from './features/landing/DropZone'
import StagingList from './features/landing/StagingList'
import AuthPage from './features/auth/AuthPage'
import { matchAccountPath } from './features/auth/authRoute'
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

  const goHome = () => {
    window.history.pushState('', '', '/')
    window.dispatchEvent(new PopStateEvent('popstate')) // existing popstate listener updates pathname.
  }

  return (
    <>
      <TopBar
        profile={session}
        onNavigateAccount={() => {
          window.history.pushState('', '', '/account')
          window.dispatchEvent(new PopStateEvent('popstate'))
        }}
        onSignOut={() => {
          clearSession()
          setSession(null)
        }}
      />
      <main>
        {unsubscribeToken !== null ? (
          <UnsubscribePage token={unsubscribeToken} />
        ) : matchAccountPath(pathname) ? (
          // Catch-all until T-008-07 takes over the section; keep matchAccountPath a pure predicate.
          <AuthPage onSignedIn={(p) => { setSession(p); goHome() }} />
        ) : linkId !== null ? (
          <RecipientPage linkId={linkId} />
        ) : (
          <>
            <DropZone />
            {hasFiles && <StagingList />}
          </>
        )}
      </main>
    </>
  )
}
