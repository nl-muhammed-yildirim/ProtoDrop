import { expect, test } from '@playwright/test'

import { mintDownload, publicGet, readSession, seedTransfer, withFlagOverride } from './api'
import { FLAGS_TTL_SECONDS, RUN_ID, locale, requireRemote } from './env'

const FLAG_KEY = 'limits.free.maxDownloads' // TA-13.2 seeded key.
const POLL_EVERY_MS = 10_000

/**
 * T-028 (D8): "expiry via admin flag override". Retention is whole days, so time-based expiry cannot be observed inside a test
 * (covered by integration tests + local lifecycle.spec). What an admin flag CAN drive within a test is the terminal recipient
 * state: limits.free.maxDownloads = 1 is stamped at send, one counted download flips the row to the download-limit state (410).
 * Needs a SuperAdmin session; the role requires a CONFIRMED email (owner prerequisite, tests/load/README.md). When setup could not
 * obtain it, this spec SKIPS with that reason — it never tries to work around the confirmation.
 */
test.describe('expiry via admin flag override @staging', () => {
  requireRemote()

  test('@staging maxDownloads=1 flag -> one counted download -> terminal screen + 410', async ({ page }) => {
    test.setTimeout(240_000)
    const session = readSession()
    test.skip(session.adminToken === null, session.adminSkipReason ?? 'no admin session')
    const adminToken = session.adminToken!
    const en = locale('en')

    // Raise-then-restore: the original value is read from the API (no literal limits) and PUT back in finally.
    const seeded = await withFlagOverride(adminToken, FLAG_KEY, '1', async () => {
      // The flag cache is per replica, so poll with fresh seeds (the limit is stamped at send) instead of one fixed sleep.
      const deadline = Date.now() + (FLAGS_TTL_SECONDS + 30) * 1000
      for (let attempt = 0; ; attempt++) {
        const candidate = await seedTransfer({ files: [{ name: `e2e-${RUN_ID}-expiry-${attempt}.txt`, content: 'expiry e2e payload' }] })
        const pub = await publicGet(candidate.linkId)
        if (pub.status === 200 && pub.body.downloadsLeft === 1) return candidate
        if (Date.now() > deadline) throw new Error(`flag override never took effect within ${FLAGS_TTL_SECONDS + 30}s (last downloadsLeft=${String(pub.body.downloadsLeft)})`)
        await new Promise((resolve) => setTimeout(resolve, POLL_EVERY_MS))
      }
    })

    // Live transfers never re-resolve limits (FR-018-4), so the restore above is safe; now spend the one download via the API.
    expect(await mintDownload(seeded.linkId, seeded.files[0].fileId)).toBe(200)

    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-terminal')).toBeVisible()
    await expect(page.locator('.rp-list')).toHaveCount(0)
    await expect(page.locator('.rp-row')).toHaveCount(0)
    await expect(page.locator('.rp-terminal-action')).toHaveText(en['recipient.terminal.cta'])

    const pub = await publicGet(seeded.linkId)
    expect(pub.status).toBe(410)
    expect(pub.body.code).toBe('MAX_DOWNLOADS_REACHED')
  })
})
