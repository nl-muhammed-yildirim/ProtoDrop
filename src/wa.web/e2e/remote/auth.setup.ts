import crypto from 'node:crypto'
import fs from 'node:fs'

import { expect, test } from '@playwright/test'

import { adminRole, signup, tryLogin, writeSession } from './api'
import { ADMIN, AUTH_DIR, RUN_ID, requireRemote } from './env'

/**
 * T-028 (D4): every auth call of the run happens here, once — the API allows 5 requests/min/IP on /auth/*.
 * Owner: fresh synthetic account (signup via API, then a real UI sign-in so storageState holds wa.session + the SPA mirror).
 * A PARTIAL session file is written right after signup, so teardown can always delete the owner even if a later step fails.
 * Admin: optional E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD. The role needs a CONFIRMED email (owner step, tests/load/README.md) and must be
 * SuperAdmin (proved via GET /auth/me). Any admin problem becomes an adminSkipReason for expiry-flag.spec — never a suite failure,
 * and setup never tries to work around the confirmation.
 * The test title carries @staging so the CD `-g @staging` filter keeps this dependency.
 */
test('@staging setup — owner + admin sessions', async ({ page }) => {
  requireRemote()
  test.setTimeout(240_000) // authPost may wait out one Retry-After (<= 65 s) per call.
  fs.mkdirSync(AUTH_DIR, { recursive: true })

  const ownerEmail = `e2e-owner-${RUN_ID}@example.com`
  const ownerPassword = crypto.randomUUID()
  let ownerToken = await signup(ownerEmail, ownerPassword)
  writeSession({ ownerEmail, ownerToken, adminToken: null, adminSkipReason: 'setup incomplete' }) // partial: lets teardown clean up.

  await page.goto('/account')
  await page.locator('#auth-email').fill(ownerEmail)
  await page.locator('#auth-password').fill(ownerPassword)
  const loginResponse = page.waitForResponse((r) => r.url().includes('/auth/login') && r.request().method() === 'POST')
  await page.locator('.au-continue-btn').click()
  const res = await loginResponse
  expect(res.ok(), `UI sign-in: ${res.status()}`).toBe(true)
  ownerToken = ((await res.json()) as { sessionToken: string }).sessionToken
  await expect(page).not.toHaveURL(/\/account/)
  await page.context().storageState({ path: `${AUTH_DIR}owner.json` })

  let adminToken: string | null = null
  let adminSkipReason: string | null = null
  if (!ADMIN) {
    adminSkipReason = 'E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD not set'
  } else {
    try {
      let token: string | null = null
      const attempt = await tryLogin(ADMIN.email, ADMIN.password)
      if (attempt.ok) {
        token = attempt.token
      } else if (attempt.status === 401 && attempt.code === 'WRONG_PASSWORD') {
        // Either the account does not exist yet (signup creates it and returns a session) or the secret is wrong
        // (signup then fails with 401 WRONG_PASSWORD too, which is caught below and becomes the skip reason).
        token = await signup(ADMIN.email, ADMIN.password)
      } else {
        adminSkipReason = `admin login failed (${attempt.status} ${attempt.code ?? ''})`
      }
      if (token) {
        const role = await adminRole(token)
        if (role === 'superadmin') adminToken = token
        else adminSkipReason =
          `E2E_ADMIN_EMAIL logs in but is not a SuperAdmin (role: ${role ?? 'none'}). The role needs a CONFIRMED email: ` +
          'the owner must confirm that account once via magic link (tests/load/README.md, "Owner prerequisite"), and it must be in ADMIN_EMAILS.'
      }
    } catch (err) {
      adminSkipReason = `admin bootstrap failed — check E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD (${err instanceof Error ? err.message.slice(0, 200) : 'error'})`
    }
  }

  writeSession({ ownerEmail, ownerToken, adminToken, adminSkipReason })
})
