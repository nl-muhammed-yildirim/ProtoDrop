import { expect, test } from '@playwright/test'

import { captureDraft, isStagePut, makeSparseFiles } from './helpers'

/**
 * T-016-02 (F-TRF-016 / FR-016-3): aria-live e2e — real stack. A MutationObserver installed via addInitScript
 * records every textContent change on [role=status]/[role=alert] elements into window.__ariaLog, so the tests
 * assert what screen readers actually hear DURING the run (no fixed sleeps; waitForFunction polls).
 */

/** Installs the live-region recorder before any page script runs — must precede navigation. */
function installAriaRecorder(page: import('@playwright/test').Page): void {
  page.addInitScript(() => {
    const w = window as unknown as { __ariaLog?: Array<{ role: string; text: string }> }
    if (!w.__ariaLog) w.__ariaLog = []
    const observer = new MutationObserver((mutations) => {
      for (const m of mutations) {
        // characterData/childList changes land on the text node or its parent — climb to the live region.
        const el = m.target instanceof Element ? m.target : (m.target.parentElement ?? null)
        if (!el || !el.closest('[role="status"], [role="alert"]')) continue
        const region = el.closest('[role="status"], [role="alert"]')!
        w.__ariaLog!.push({ role: region.getAttribute('role')!, text: region.textContent ?? '' })
      }
    })
    observer.observe(document.documentElement, { subtree: true, characterData: true, childList: true })
  })
}

test.describe('T-016-02 aria-live e2e', { mode: 'serial' }, () => {
  // D-2 + D-3: an injected upload failure announces throttle points (role=status) and the failed file by name.
  test('injected upload failure announces progress points + failed file', async ({ page }) => {
    const fixtures = makeSparseFiles([
      { name: 'aria fail.bin', sizeBytes: 4096 }, // one 8 MiB block → every attempt targets the same minted path.
      { name: 'good sibling.txt', sizeBytes: 1024 },
    ])

    try {
      installAriaRecorder(page)
      await page.goto('/')
      const attempts = { n: 0 }
      let failSuffix = ''
      let aborting = false
      // Route handler reads the closure per request — arm before knowing the minted path (AC-001-3 pattern).
      await page.route('**/*', async (route) => {
        if (aborting && isStagePut(route.request(), failSuffix)) {
          attempts.n += 1
          return route.abort()
        }
        await route.continue()
      })

      const draftPromise = captureDraft(page) // one draft POST for both files.
      await page.setInputFiles('input[type="file"]', fixtures.paths)
      await expect(page.locator('.sl-row')).toHaveCount(2)
      await page.click('.sl-send')

      const draft = await draftPromise
      failSuffix = new URL(draft.files[0].uploadUrl).pathname // failing file is first in row order.
      aborting = true

      // Poll until the recorder has BOTH: a status-region progress announcement (throttle point) AND the failed-file line.
      await page.waitForFunction(() => {
        const log = (window as unknown as { __ariaLog?: Array<{ role: string; text: string }> }).__ariaLog ?? []
        const hasProgress = log.some((e) => e.role === 'status' && /Uploading/.test(e.text)) // EC-016-1 milestone copy.
        const hasFailedFile = log.some((e) => (e.role === 'alert' || e.role === 'status') && /aria fail\.bin|failed/i.test(e.text))
        return hasProgress && hasFailedFile
      }, null, { timeout: 60_000 })

      // Read the recorded announcements back and assert both are present (role + text).
      const log = await page.evaluate(() => (window as unknown as { __ariaLog?: Array<{ role: string; text: string }> }).__ariaLog ?? [])
      expect(log.some((e) => e.role === 'status' && /Uploading/.test(e.text)), 'a status-region progress announcement').toBe(true) // D-2.
      expect(
        log.some((e) => (e.role === 'alert' || e.role === 'status') && /aria fail\.bin|failed/i.test(e.text)),
        `the failed file is announced by name — ${JSON.stringify(log)}`,
      ).toBe(true) // D-3.

      // The failure row is visible too — the announcement mirrors what's on screen (FR-016-3).
      await expect(page.locator('.sl-row-failed')).toBeVisible()
    } finally {
      fixtures.cleanup()
    }
  })

  // FR-016-5 + toastRole single-source: error toasts carry role=alert, success/info toasts carry role=status.
  test('toast roles in DOM — over-limit message is alert; info toast is status', async ({ page }) => {
    const fixtures = makeSparseFiles([
      { name: 'a.bin', sizeBytes: 2 * 1073_741_824 }, // three × 2 GiB = 6 GiB total — over the free-plan limit.
      { name: 'b.bin', sizeBytes: 2 * 1073_741_824 },
      { name: 'c.bin', sizeBytes: 2 * 1073_741_824 },
    ])

    try {
      installAriaRecorder(page)
      await page.goto('/')
      await page.setInputFiles('input[type="file"]', fixtures.paths)
      // The over-limit message is an assertive alert (pinned by AC-001-2's selector — role from toastRole('error')).
      await expect(page.locator('.sl-limit-msg[role=alert]')).toBeVisible()

      // Success/info toasts are polite status regions: trigger the skip-toast via an empty-folder drop (AC-001-4 shape).
      await page.evaluate(() => {
        const zone = document.querySelector('.dz')
        if (!zone) throw new Error('drop zone missing')
        const evt = new DragEvent('drop', { bubbles: true, cancelable: true })
        Object.defineProperty(evt, 'dataTransfer', { value: { files: [], entries: [{}] } }) // handler reads only length/index (DropSource).
        zone.dispatchEvent(evt)
      })
      await expect(page.locator('.dz-toast[role=status]')).toHaveText('Some files were skipped')
    } finally {
      fixtures.cleanup()
    }
  })
})
