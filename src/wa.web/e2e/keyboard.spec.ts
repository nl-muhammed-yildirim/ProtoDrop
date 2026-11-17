import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

import { makeSparseFiles } from './helpers'

/**
 * T-016-04 (F-TRF-016 / AC-016-1): keyboard-only full guest flow — real stack. One serial test walks the exact chain:
 * seed files → remove one → Send → add recipient → Send transfer → copy link, using ONLY Tab/Shift+Tab/Enter/type
 * after seeding (page.setInputFiles is the sanctioned stand-in for the native file picker). No mouse events; no
 * positive tabindex anywhere — every stop in the tab sequence must be a natural DOM-order focusable.
 */

/** Installs the live-region recorder before any page script runs — must precede navigation (ariaLive.spec.ts pattern). */
function installAriaRecorder(page: Page): void {
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

/** The focusable stops this flow can land on — matched in order so 'btn-primary' (dz-button AND sl-send) disambiguates. */
const FOCUS_STOPS = [
  '.tb-signin', '.dz-button', '.sl-remove', '.sl-send', '.ls-copy-btn', '#recipients-textarea',
  '#from-name', '#sender-email', '#note-textarea', '#link-password', '.ls-password-toggle', '.ls-send-btn',
] as const

/** Returns the selector of whichever known stop holds focus, or 'body'/'other:<tag>' when none does. */
function focusStop(page: Page): Promise<string> {
  return page.evaluate((stops) => {
    const el = document.activeElement instanceof HTMLElement ? (document.activeElement as HTMLElement) : null
    if (!el || !el.matches('button, input, textarea')) return 'body' // <body>/<html> or an unknown focusable.
    for (const s of stops) if (el.matches(s)) return s
    return `other:${el.tagName.toLowerCase()}`
  }, [...FOCUS_STOPS])
}

/** Tabs forward until the given stop holds focus; returns true when it landed within maxSteps. */
async function tabTo(page: Page, selector: string, maxSteps = 16): Promise<boolean> {
  for (let i = 0; i < maxSteps; i++) {
    await page.keyboard.press('Tab')
    if ((await focusStop(page)) === selector) return true
  }
  return false
}

test.describe('T-016-04 keyboard-only full guest flow', { mode: 'serial' }, () => {
  test('AC-016-1 — seed → remove one → Send → recipient → Send transfer → copy link, all via Tab/Enter/type', async ({ page }) => {
    const fixtures = makeSparseFiles([
      { name: 'kbf one.bin', sizeBytes: 4096 }, // three small files — the upload finishes in seconds.
      { name: 'kbf two.txt', sizeBytes: 1024 },
      { name: 'kbf three.pdf', sizeBytes: 512 },
    ])

    try {
      installAriaRecorder(page) // before goto so the observer is live for every later region.
      await page.context().grantPermissions(['clipboard-write']) // handleCopy's navigator.clipboard path needs it in headless.
      await page.goto('/')

      // ---- Seed (programmatic — the native file picker has no keyboard path in a test). ----
      await page.setInputFiles('input[type="file"]', fixtures.paths)
      await expect(page.locator('.sl-row')).toHaveCount(3)

      // ---- Tab-sequence assertion: from body, tab to "Choose files", then record every stop until Send. ----
      const reachedDz = await tabTo(page, '.dz-button') // passes TopBar's Sign-in naturally — no tabindex tricks needed.
      expect(reachedDz, 'tabbing from body must land on the Choose files button').toBe(true)
      const stops: string[] = []
      let landedSend = false
      for (let i = 0; i < 16 && !landedSend; i++) {
        await page.keyboard.press('Tab')
        if ((await focusStop(page)) === '.sl-send') { landedSend = true } else stops.push(await focusStop(page))
      }
      // The file input is display:none — Chromium's tab order skips it, so the natural sequence between dz-button and
      // Send is exactly the three remove buttons. Any positive-tabindex insertion or DOM reorder breaks this list.
      expect(landedSend).toBe(true)
      expect(stops).toEqual(['.sl-remove', '.sl-remove', '.sl-remove'])

      // ---- Remove one file (row 1): Shift+Tab back to it, Enter; focus must survive removal (D-2 fix). ----
      for (let i = 0; i < 3; i++) await page.keyboard.press('Shift+Tab') // sl-send → row 3 → row 2 → row 1.
      expect(await focusStop(page), 'focus should be on row 1\'s remove button').toBe('.sl-remove')
      const removeOne = page.locator('.sl-remove[aria-label^="Remove kbf one"]') // pins exactly this button (en locale).
      await expect(removeOne).toHaveCount(1)
      await page.keyboard.press('Enter')
      await expect(page.locator('.sl-row')).toHaveCount(2)
      // Poll — the D-2 fix focuses via requestAnimationFrame (next frame); a one-shot read could race it. If the fix is
      // absent, focus sits on <body> and this times out with the message below.
      await expect.poll(() => page.evaluate(() => (document.activeElement ? document.activeElement.tagName : 'NULL')), { timeout: 5_000 })
        .not.toBe('BODY')

      // ---- Send: tab forward from the surviving remove button to .sl-send and Enter. ----
      const reachedSend = await tabTo(page, '.sl-send')
      expect(reachedSend, 'tabbing forward must reach Send').toBe(true)
      await expect(page.locator('.sl-send')).toBeEnabled() // 2 files remain — under the limit, so enabled.
      await page.keyboard.press('Enter')

      // ---- Upload completion + handoff focus (T-016-03 D-7: LinkScreen mounts with Copy focused). ----
      const heading = page.locator('.ls-heading')
      await expect(heading).toContainText(/Your link is ready/, { timeout: 60_000 }) // still the form phase — not yet sent.
      await expect(page.locator('.ls-copy-btn')).toHaveCount(1)
      // Poll — D-7's focus() runs in a post-commit useEffect; it may land after this render is visible. If T-016-03's
      // mount-focus regresses, the button never receives focus and this times out.
      await expect.poll(() => focusStop(page), { timeout: 5_000 })
        .toBe('.ls-copy-btn')

      // ---- Add recipient: Tab to #recipients-textarea and type. ----
      const reachedRecipients = await tabTo(page, '#recipients-textarea', 4) // the next focusable after Copy is the textarea.
      expect(reachedRecipients).toBe(true)
      await page.keyboard.type('test@example.com')

      // ---- Send transfer: tab through sender fields to .ls-send-btn and Enter. ----
      const reachedSendTransfer = await tabTo(page, '.ls-send-btn', 10) // from-name → sender-email → note → password → toggle → send.
      expect(reachedSendTransfer, 'tabbing from the recipients field must reach Send transfer').toBe(true)
      await page.keyboard.press('Enter')

      // ---- Sent state: D-3 mount focus puts focus on the sent-state Copy button. ----
      const headingSent = page.locator('.ls-heading')
      await expect(headingSent).toContainText(/Transfer sent/, { timeout: 30_000 })
      const sentCopyBtn = page.locator('.ls-copy-btn')
      await expect(sentCopyBtn).toHaveCount(1) // the form-phase button is gone — only the sent-state one remains.
      // Poll — D-3's focus() runs in a post-commit useEffect after setSendState('sent'); if it regresses, focus stays on <body>.
      await expect.poll(() => focusStop(page), { timeout: 5_000 })
        .toBe('.ls-copy-btn')

      // ---- Copy link in sent state: Enter → visible confirmation + live-region announcement (FR-016-3). ----
      await page.keyboard.press('Enter')
      const copySuccess = page.locator('.ls-copy-success[role="status"]')
      await expect(copySuccess).toContainText(/Copied/) // the 2 s revert window is generous for this assertion.
      await page.waitForFunction(() => {
        const log = (window as unknown as { __ariaLog?: Array<{ role: string; text: string }> }).__ariaLog ?? []
        return log.some((e) => e.role === 'status' && /Copied/.test(e.text)) // the polite region announced it.
      }, null, { timeout: 10_000 })
    } finally {
      fixtures.cleanup()
    }
  })
})
