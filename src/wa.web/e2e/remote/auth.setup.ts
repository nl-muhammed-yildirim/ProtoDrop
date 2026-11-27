import crypto from 'node:crypto'
import fs from 'node:fs'

import { expect, test } from '@playwright/test'

import { probeAdmin, signup, tryLogin, writeSession } from './api'
import { ADMIN, AUTH_DIR, RUN_ID, requireRemote } from './env'

/**
 * T-028 (D4): every auth call of the run happens here, once — the API allows 5 requests/min/IP on /auth/*.
 * Owner: fresh synthetic account (signup via API, then a real UI sign-in so storageState holds wa.session + the SPA mirror).
 * Admin: optional E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD; the role needs a CONFIRMED email (owner step, see tests/load/README.md),
 * so a login that lacks the role is recorded as a skip reason for expiry-flag.spec — setup never tries to work around it.
 * The test title carries @staging so the CD `-g @staging` filter keeps this dependency.
 */
test('@staging setup — owner + admin sessions', async ({ page }) => {
  requireRemote()
  test.setTimeout(240_000) // authPost may wait out one Retry-After (<= 65 s) per call.
  fs.mkdirSync(AUTH_DIR, { recursive: true })

  const ownerEmail = `e2e-owner-${RUN_ID}@example.com`
  const ownerPassword = crypto.randomUUID()
  await signup(ownerEmail, ownerPassword)

  await page.goto('/account')
  await page.locator('#auth-email').fill(ownerEmail)
  await page.locator('#auth-password').fill(ownerPassword)
  const loginResponse = page.waitForResponse((r) => r.url().includes('/auth/login') && r.request().method() === 'POST')
  await page.locator('.au-continue-btn').click()
  const res = await loginResponse
  expect(res.ok(), `UI sign-in: ${res.status()}`).toBe(true)
  const ownerToken = ((await res.json()) as { sessionToken: string }).sessionToken
  await expect(page).not.toHaveURL(/\/account/)
  await page.context().storageState({ path: `${AUTH_DIR}owner.json` })

  let adminToken: string | null = null
  let adminSkipReason: string | null = null
  if (!ADMIN) {
    adminSkipReason = 'E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD not set'
  } else {
    let attempt = await tryLogin(ADMIN.email, ADMIN.password)
    if (!attempt.ok && attempt.status === 401 && attempt.code === 'WRONG_PASSWORD') {
      // First run: the account does not exist yet — create it (a known email would fail again below, which is a clear error).
      await signup(ADMIN.email, ADMIN.password)
      attempt = await tryLogin(ADMIN.email, ADMIN.password)
    }
    if (!attempt.ok) {
      adminSkipReason = `admin login failed (${attempt.status} ${attempt.code ?? ''}) — check E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD`
    } else {
      const probe = await probeAdmin(attempt.token)
      if (probe.ok) adminToken = attempt.token
      else adminSkipReason =
        `E2E_ADMIN_EMAIL logs in but lacks the admin role (GET /admin/flags -> ${probe.status}). ` +
        'The role needs a CONFIRMED email: the owner must confirm that account once via magic link (tests/load/README.md, "Owner prerequisite").'
    }
  }

  writeSession({ ownerEmail, ownerToken, adminToken, adminSkipReason })
})
