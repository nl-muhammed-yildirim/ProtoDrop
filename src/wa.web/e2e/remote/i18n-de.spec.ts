import { expect, test } from '@playwright/test'

import { seedTransfer } from './api'
import { BASE_URL, RUN_ID, locale, requireRemote } from './env'

const de = locale('de')
const UNKNOWN_LINK_ID = 'e2e7kqw2' // valid TA-9.6 charset, never issued (same constant as e2e/sql.ts).

/** T-028 (D7): i18n spot-check — a de-DE browser gets German copy on landing, an active link and a terminal screen, with no missing-key warnings. */
test.describe('i18n de spot-check @staging', () => {
  requireRemote()

  test('@staging de-DE locale renders German copy and no missing i18n keys', async ({ browser }) => {
    const seeded = await seedTransfer({
      files: [
        { name: `e2e-${RUN_ID}-de-a.txt`, content: 'de a' },
        { name: `e2e-${RUN_ID}-de-b.txt`, content: 'de b' },
      ],
    })

    const context = await browser.newContext({ baseURL: BASE_URL, locale: 'de-DE' })
    try {
      const page = await context.newPage()
      const missing: string[] = []
      page.on('console', (m) => { if (m.text().includes('[i18n] missing key')) missing.push(m.text()) })

      await page.goto('/')
      await expect(page.getByText(de['landing.dropzone.title'], { exact: true })).toBeVisible()

      await page.goto(`/t/${seeded.linkId}`)
      await expect(page.locator('.rp-download-all')).toHaveText(de['recipient.active.downloadAll'])

      await page.goto(`/t/${UNKNOWN_LINK_ID}`)
      await expect(page.locator('.rp-terminal-action')).toHaveText(de['recipient.terminal.cta'])

      expect(missing, 'missing i18n keys').toEqual([])
    } finally {
      await context.close()
    }
  })
})
