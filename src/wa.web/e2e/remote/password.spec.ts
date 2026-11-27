import { expect, test } from '@playwright/test'

import { seedTransfer } from './api'
import { RUN_ID, requireRemote } from './env'

/** T-028 (D7): password flow — seeded via the public API, asserted through the UI; mirrors local recipient.spec.ts AC-003-2. */
test.describe('password flow @staging', () => {
  requireRemote()

  test('@staging wrong password errors, right password unlocks and survives a reload', async ({ page }) => {
    const password = `Pw-${RUN_ID}-x9!`
    const seeded = await seedTransfer({
      files: [{ name: `e2e-${RUN_ID}-locked.txt`, content: 'locked e2e payload' }],
      password,
    })

    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-password-card')).toBeVisible()
    await expect(page.locator('.rp-list')).toBeHidden()

    await page.locator('#rp-password-input').fill(`${password}-wrong`)
    await page.locator('.rp-password-submit').click()
    await expect(page.locator('.rp-password-error')).toBeVisible()
    await expect(page.locator('.rp-list')).toBeHidden()

    await page.locator('#rp-password-input').fill(password)
    await page.locator('.rp-password-submit').click()
    await expect(page.locator('.rp-list')).toBeVisible()

    await page.reload()
    await expect(page.locator('.rp-list')).toBeVisible()
    await expect(page.locator('.rp-password-card')).toBeHidden()
  })
})
