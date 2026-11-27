import { test } from '@playwright/test'

import { deleteMe, readSessionOrNull } from './api'
import { requireRemote } from './env'

/**
 * T-028 (D4): deletes the run's synthetic owner (DELETE /auth/me, tolerates 401/404). Setup writes a partial session file right
 * after signup, so even an aborted setup leaves a token here; a missing file just means nothing was created. The admin is kept.
 */
test('@staging teardown — delete run owner', async () => {
  requireRemote()
  const session = readSessionOrNull()
  if (!session?.ownerToken) return
  await deleteMe(session.ownerToken)
})
