import { useEffect } from 'react'

import { loadPlanLimits } from './loadPlanLimits'
import { resetPlanLimits } from './planLimits'

interface PlanLimitsSyncProps {
  signedIn: boolean
  /** Called after the user's limits were applied, so readers of getPlanLimits() can re-render. */
  onLoaded: () => void
}

/**
 * Renders nothing. Mounted on the landing page: a signed-in user's per-upload caps are loaded from
 * GET /billing/plan before files are staged; guests (and sign-out) fall back to the Free defaults.
 * A failed load keeps the defaults and never blocks the page.
 */
export default function PlanLimitsSync({ signedIn, onLoaded }: PlanLimitsSyncProps) {
  useEffect(() => {
    if (!signedIn) {
      resetPlanLimits()
      return
    }
    let cancelled = false
    void loadPlanLimits().then((applied) => {
      if (applied && !cancelled) onLoaded()
    })
    return () => { cancelled = true }
    // onLoaded is a fresh closure each render; only the session state should retrigger the load.
  }, [signedIn])
  return null
}
