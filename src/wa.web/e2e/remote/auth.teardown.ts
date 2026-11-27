import { test } from '@playwright/test'

import { deleteMe, readSession } from './api'
import { requireRemote } from './env'

/** T-028 (D4): deletes the run's synthetic owner (DELETE /auth/me, tolerates 401/404). The admin account is long-lived and kept. */
test('@staging teardown — delete run owner', async () => {
  requireRemote()
  await deleteMe(readSession().ownerToken)
})
