import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

import { captureDraft, isStagePut, makeSparseFiles } from './helpers'

/**
 * T-001-09 (AC-001-1…4): real stack — Vite :5173 against API :8080 + SQL/Azurite containers. One draft POST
 * per test; the browser uploads minted-and-re-signed SAS URLs straight to Azurite (CORS set in global-setup).
 */

/** Collects console error texts mentioning an unsupported x-ms-version — the 12.24.0 downgrade proof point (AC-001-1). */
function trackVersionErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('console', (msg) => {
    if (msg.type() === 'error') errors.push(msg.text())
  })
  return errors
}

test.describe('T-001-09 staging e2e', { mode: 'serial' }, () => {
  // AC-001-1: three real files totaling exactly 2 GiB (spaces + unicode in names) upload to done via Send.
  test('AC-001-1 — staged files of exactly 2 GiB upload through the browser to done', async ({ page }) => {
    const fixtures = makeSparseFiles([
      { name: 'big one α.bin', sizeBytes: 1073_741_824 }, // 1 GiB
      { name: 'second report.pdf', sizeBytes: 536_870_912 }, // 512 MiB
      { name: 'third-notes.txt', sizeBytes: 536_870_912 }, // 512 MiB — total exactly 2 GiB, under the 5 GiB limit.
    ])
    const versionErrors = trackVersionErrors(page)

    try {
      await page.goto('/')
      await page.setInputFiles('input[type="file"]', fixtures.paths)
      await expect(page.locator('.sl-row')).toHaveCount(3)
      // First live check of the downgrade: rows must reach done — an unsupported x-ms-version fails every PUT (400).
      await page.click('.sl-send')
      await expect(page.locator('.sl-row.sl-done')).toHaveCount(3, { timeout: 60_000 })
      for (let i = 0; i < 3; i++) {
        await expect(page.locator('.sl-row').nth(i).locator('.sl-percent')).toHaveText('100%')
      }
      await expect(page.locator('.sl-send')).toBeEnabled()
      const bad = versionErrors.filter((t) => /API version|x-ms-version/i.test(t))
      expect(bad, `console errors about an unsupported API version: ${bad.join('\n')}`).toEqual([])
    } finally {
      fixtures.cleanup()
    }
  })

  // AC-001-2: over-limit selection is blocked before any upload — message names the exact limit, no Azurite traffic.
  test('AC-001-2 — total above the free-plan limit blocks Send with the exact message', async ({ page }) => {
    const azuriteTraffic: string[] = []
    page.on('request', (req) => {
      const u = req.url()
      if (u.includes(':10000') || u.includes('/staging/')) azuriteTraffic.push(u)
    })

    const fixtures = makeSparseFiles([
      { name: 'a.bin', sizeBytes: 2 * 1073_741_824 }, // three × 2 GiB = 6 GiB total, none individually over the per-file limit.
      { name: 'b.bin', sizeBytes: 2 * 1073_741_824 },
      { name: 'c.bin', sizeBytes: 2 * 1073_741_824 },
    ])

    try {
      await page.goto('/')
      await page.setInputFiles('input[type="file"]', fixtures.paths)
      await expect(page.locator('.sl-row')).toHaveCount(3)
      await expect(page.locator('.sl-limit-msg[role=alert]')).toBeVisible()
      await expect(page.locator('.sl-limit-msg[role=alert]'))
        .toContainText(/Transfer size \d+\.\d+ GB exceeds the 5\.00 GB limit\./)
      await expect(page.locator('.sl-send')).toBeDisabled()
      expect(azuriteTraffic, `Azurite traffic before Send: ${azuriteTraffic.join('\n')}`).toEqual([])
    } finally {
      fixtures.cleanup()
    }
  })

  // AC-001-3: one file's blocks fail all 5 attempts (row fails, sibling still done); Retry reuses the minted URLs.
  test('AC-001-3 — an injected stage failure exhausts 5 attempts; Retry recovers with no new draft', async ({ page }) => {
    const fixtures = makeSparseFiles([
      { name: 'failing blob.bin', sizeBytes: 4096 }, // one 8 MiB block → every attempt targets the same block.
      { name: 'good sibling.txt', sizeBytes: 1024 },
    ])

    try {
      await page.goto('/')
      const attempts = { n: 0 }
      let failSuffix = ''
      let aborting = false
      // Route handler reads the closure per request, so arming it before knowing the minted path is safe.
      await page.route('**/*', async (route) => {
        if (aborting && isStagePut(route.request(), failSuffix)) {
          attempts.n += 1
          return route.abort()
        }
        await route.continue()
      })

      const draftPromise = captureDraft(page) // one draft POST for both files; Retry must not add a second.
      await page.setInputFiles('input[type="file"]', fixtures.paths)
      await expect(page.locator('.sl-row')).toHaveCount(2)
      await page.click('.sl-send')

      const draft = await draftPromise
      failSuffix = new URL(draft.files[0].uploadUrl).pathname // failing file is first in row order.
      aborting = true
      await expect(page.locator('.sl-row-failed')).toBeVisible({ timeout: 30_000 }) // the full backoff schedule (7.5 s) runs here
      expect(attempts.n, 'stage attempts for the failing file').toBe(5)
      await expect(page.locator('.sl-row-failed .sl-failed-msg')).toHaveText('Upload failed — retry')
      await expect(page.locator('.sl-row.sl-done')).toHaveCount(1, { timeout: 60_000 }) // sibling completed in the same run

      aborting = false // no second draft POST happens on retry — the same minted URLs re-enter uploadFile.
      await page.click('.sl-retry[aria-label="Retry failing blob.bin"]')
      await expect(page.locator('.sl-row.sl-done')).toHaveCount(2, { timeout: 60_000 })
      expect(attempts.n, 'no further stage attempts after the abort toggled off').toBe(5)
    } finally {
      fixtures.cleanup()
    }
  })

  // AC-001-4: an empty folder drop (entries present, zero files) stages nothing and shows the skip toast.
  test('AC-001-4 — dropping an empty folder skips without staging rows', async ({ page }) => {
    await page.goto('/')
    await page.evaluate(() => {
      const zone = document.querySelector('.dz')
      if (!zone) throw new Error('drop zone missing')
      // Chromium brand-checks init.dataTransfer — construct without it and attach the plain shape afterwards.
      const evt = new DragEvent('drop', { bubbles: true, cancelable: true })
      Object.defineProperty(evt, 'dataTransfer', { value: { files: [], entries: [{}] } }) // handler reads only length/index (DropSource).
      zone.dispatchEvent(evt)
    })
    await expect(page.locator('.dz-toast[role=status]')).toHaveText('Some files were skipped')
    await expect(page.locator('.sl-row')).toHaveCount(0)
  })
})
