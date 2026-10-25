import { useEffect, useState } from 'react'

import { useUploadEngine } from './core/upload/UploadEngine'
import DropZone from './features/landing/DropZone'
import StagingList from './features/landing/StagingList'
import RecipientPage from './features/recipient/RecipientPage'
import UnsubscribePage from './features/unsubscribe/UnsubscribePage'
import { matchLinkPath } from './features/recipient/recipientRoute'
import { matchUnsubscribePath } from './features/unsubscribe/unsubscribeRoute'

/**
 * T-003-02 (FR-003-1): pathname routing — /t/{linkId} → recipient page; landing unchanged at /.
 * Read-once + popstate refresh: the app has no router yet, so this is the minimal change that keeps
 * pushState (tests) and browser back/forward both working. The typeof guard keeps the SSR test
 * (App.test.tsx) hermetic — it renders App in node where window does not exist.
 */
export default function App() {
  const [pathname, setPathname] = useState(() => (typeof window === 'undefined' ? '/' : window.location.pathname))

  useEffect(() => {
    const onPop = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  const linkId = matchLinkPath(pathname)
  const unsubscribeToken = matchUnsubscribePath(pathname)
  const hasFiles = useUploadEngine((s) => s.files.length > 0)

  return (
    <main>
      {unsubscribeToken !== null ? (
        <UnsubscribePage token={unsubscribeToken} />
      ) : linkId !== null ? (
        <RecipientPage linkId={linkId} />
      ) : (
        <>
          <DropZone />
          {hasFiles && <StagingList />}
        </>
      )}
    </main>
  )
}
