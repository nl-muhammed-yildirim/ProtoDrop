import { expect, test } from '@playwright/test'

import { noteUiDraft, publicGet, readSession, seedTransfer } from './api'
import { AUTH_DIR, RUN_ID, locale, requireRemote } from './env'

const en = locale('en')

/** T-028 (D7): My Files create / read / update (re-send) / delete with the run's owner session (storageState from staging-setup). */
test.describe('my files CRUD @staging', () => {
  requireRemote()
  test.use({ storageState: `${AUTH_DIR}owner.json` })

  test('@staging owned transfer is listed, re-sent, and deleted', async ({ page }) => {
    const name = `e2e-${RUN_ID}-myfiles.txt`
    // C: create through the real API, owned by the run's owner.
    const seeded = await seedTransfer({ files: [{ name, content: 'my files e2e payload' }], sessionToken: readSession().ownerToken })

    // R
    await page.goto('/files')
    const rows = page.locator('.mf-row').filter({ hasText: name })
    await expect(rows).toHaveCount(1)

    // U: Re-send -> the resend screen mints a new draft; link-only send; back on /files there are two rows.
    await noteUiDraft() // Re-send mints a new draft (and finalizes) through Front Door.
    await rows.first().getByRole('button', { name: en['files.action.resend'] }).click()
    await expect(page.locator('.ls-send-btn')).toBeVisible({ timeout: 60_000 })
    await page.locator('.ls-send-btn').click()
    await expect(page.locator('.ls-heading')).toHaveText(en['link.ready.transferSent'], { timeout: 60_000 })
    await page.goto('/files')
    await expect(rows).toHaveCount(2)

    // D: delete both rows (which row is the original is not assumed), each with one fewer row afterwards.
    for (const remaining of [1, 0]) {
      await rows.first().getByRole('button', { name: en['files.action.deleteRow'] }).click()
      await page.locator('.pf-modal-confirm').click()
      await expect(rows).toHaveCount(remaining)
    }
    expect((await publicGet(seeded.linkId)).status).toBe(404)
  })
})
