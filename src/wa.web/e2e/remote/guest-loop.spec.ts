import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'

import { expect, test } from '@playwright/test'

import { BASE_URL, RUN_ID, locale, requireRemote } from './env'

const en = locale('en')

/**
 * T-028 (D7): guest upload -> link -> download on a SECOND context (no cookie). The whole loop runs through the UI, so it
 * exercises browser blob PUT/GET against real storage (Storage CORS + CSP connect-src must allow it — plan R2).
 */
test.describe('guest loop @staging', () => {
  requireRemote()

  test('@staging guest uploads, activates the link, a cookieless context downloads identical bytes', async ({ page, browser }) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'protodrop-e2e-'))
    const name = `e2e-${RUN_ID}-guest.txt`
    const content = `ProtoDrop e2e guest loop ${RUN_ID}\n`.repeat(64)
    const file = path.join(dir, name)
    fs.writeFileSync(file, content)
    try {
      await page.goto('/')
      await page.setInputFiles('input[type="file"]', file)
      await expect(page.locator('.sl-row')).toHaveCount(1)
      await page.locator('.sl-send').click()
      await expect(page.locator('.ls-heading')).toHaveText(en['link.ready.linkReady'], { timeout: 120_000 })

      // Link-only activation: no recipients, so Send transfer activates the link without any email.
      await page.locator('.ls-send-btn').click()
      await expect(page.locator('.ls-heading')).toHaveText(en['link.ready.transferSent'], { timeout: 60_000 })
      const shown = (await page.locator('.ls-url').textContent())?.trim() ?? ''
      const linkPath = /\/t\/[0-9a-hjkmnpqrstvwxyz]{8}$/.exec(shown)?.[0]
      expect(linkPath, `link shown on the sent screen: "${shown}"`).toBeTruthy()

      // Second context: empty storage, no session — a recipient.
      const recipient = await browser.newContext({ baseURL: BASE_URL })
      try {
        expect((await recipient.cookies()).some((c) => c.name === 'wa.session')).toBe(false)
        const rp = await recipient.newPage()
        await rp.goto(linkPath!)
        await expect(rp.locator('.rp-row')).toHaveCount(1)
        await expect(rp.locator('.rp-name')).toHaveText(name)

        const [download] = await Promise.all([rp.waitForEvent('download'), rp.locator('.rp-download').click()])
        const downloadedPath = await download.path()
        expect(downloadedPath, 'download saved by Playwright').toBeTruthy()
        const downloaded = fs.readFileSync(downloadedPath)
        expect(downloaded.equals(Buffer.from(content))).toBe(true)
        expect((await recipient.cookies()).some((c) => c.name === 'wa.session')).toBe(false)
      } finally {
        await recipient.close()
      }
    } finally {
      fs.rmSync(dir, { recursive: true, force: true })
    }
  })
})
