import { expect, test } from '@playwright/test'

/**
 * T-015-03 (F-TRF-015, FR-015-2 / AC-015-4): full-screen theme parity — Playwright checks.
 *
 * Three tests per the plan's D-3:
 * - AC-015-4: emulateMedia({ colorScheme: 'dark' }) → landing page → data-theme="dark" on first frame + body background is #0F1115 (the dark palette's --bg token value).
 * - AC-015-1: account theme=dark in the session mirror ('protoDrop.session', same shape session.ts writes) seeded via addInitScript before navigation → data-theme="dark" on first frame — no light flash.
 * - AC-015-3: system mode + emulateMedia switch light→dark mid-session → data-theme flips to "dark" WITHOUT a page reload (exercises T-015-02's useSystemTheme matchMedia listener in a real browser).
 *
 * First-frame proof: addInitScript patches Element.prototype.setAttribute at document-start (before <html> even exists) and records the FIRST data-theme value ever set on <html>. The inline bootstrap in index.html is the sole writer before paint (FR-015-3), so a recorded "dark" can only come from it.
 */

/** Seeds localStorage with the full StoredProfile shape session.ts writes (core/auth/session.ts) BEFORE navigation. */
function seedSessionMirror(page: import('@playwright/test').Page, theme: 'system' | 'light' | 'dark') {
  page.addInitScript((t) => {
    window.localStorage.setItem(
      'protoDrop.session',
      JSON.stringify({ userId: 'e2e-theme-parity', email: 'theme@protodrop.local', displayName: 'Theme Parity E2E', theme: t, planCode: 'free' }),
    )
  }, theme)
}

/** Installs the first-frame recorder (see file header). Must be called before navigation. */
function recordFirstFrameTheme(page: import('@playwright/test').Page) {
  page.addInitScript(() => {
    const w = window as unknown as Record<string, string | null>
    w.themeParityFirstSet = null
    // documentElement is still null at this point (document-start), so observe via the prototype:
    // every setAttribute('data-theme', …) on <html> funnels through here — including the inline bootstrap's.
    const proto = Element.prototype as unknown as { setAttribute(name: string, value: string): void }
    // Keep original UNBOUND and call it with the real receiver (this) — binding to the prototype would make this=Element.prototype, which is not a valid DOM node → "Illegal invocation".
    const original = proto.setAttribute
    ;(Element.prototype as unknown as Record<string, unknown>).setAttribute = function (this: HTMLElement, name: string, value: string) {
      if ((this === document.documentElement || this.tagName === 'HTML') && name === 'data-theme' && w.themeParityFirstSet === null) {
        ;(w as Record<string, unknown>).themeParityFirstSet = String(value)
      }
      return original.call(this, name, value)
    }
  })
}

/** Reads a window property the tests set via evaluate — typed so no-explicit-any stays clean. */
function readWindowFlag(page: import('@playwright/test').Page, name: string): Promise<unknown> {
  return page.evaluate((n) => (window as unknown as Record<string, unknown>)[n], name)
}

/** Sets a window property the tests use to detect an in-place flip vs. a full reload — typed so no-explicit-any stays clean. */
function setWindowFlag(page: import('@playwright/test').Page, name: string): Promise<void> {
  return page.evaluate((n) => { (window as unknown as Record<string, boolean | null>)[n] = true }, name).then(() => undefined)
}

test.describe('T-015-03 Theme parity E2E', () => {
  // ---- AC-015-4: dark OS preference (no account) → data-theme="dark" before paint + body bg #0F1115 ----
  test('AC-015-4 — emulateMedia dark resolves data-theme=dark on first frame and paints the dark --bg', async ({ page }) => {
    recordFirstFrameTheme(page)
    await page.emulateMedia({ colorScheme: 'dark' })

    await page.goto('/')

    // First-frame proof: the attribute was set by index.html's pre-paint script (the only writer before paint).
    const firstSet = await readWindowFlag(page, 'themeParityFirstSet')
    expect(firstSet, 'data-theme must be "dark" on its very first set — the T-015-01 pre-paint script resolved it before paint').toBe('dark')

    // The dark palette's --bg token (#0F1115 in tokens.css html[data-theme='dark']) is what body paints.
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(15, 17, 21)')
  })

  // ---- AC-015-1: account theme=dark wins over the OS (light here) — no light flash on first frame ----
  test('AC-015-1 — session mirror theme=dark resolves data-theme=dark on first frame even with a light OS', async ({ page }) => {
    seedSessionMirror(page, 'dark')
    recordFirstFrameTheme(page)
    await page.emulateMedia({ colorScheme: 'light' }) // hostile OS preference: the account choice must still win (EC-015-1).

    await page.goto('/')

    const firstSet = await page.evaluate(() => (window as unknown as Record<string, string | null>).themeParityFirstSet)
    expect(firstSet, 'account theme=dark must be honored by the pre-paint script over a light OS — no light flash').toBe('dark')
  })

  // ---- AC-015-3: system mode tracks live OS changes via useSystemTheme's matchMedia listener, without reload ----
  test('AC-015-3 — system mode flips data-theme to dark on a mid-session emulateMedia switch, no reload', async ({ page }) => {
    seedSessionMirror(page, 'system') // explicit system (full StoredProfile shape) → useSystemTheme registers its listener.
    recordFirstFrameTheme(page)
    await page.emulateMedia({ colorScheme: 'light' })

    await page.goto('/')
    expect(await page.evaluate(() => document.documentElement.getAttribute('data-theme'))).toBe('light') // system + light OS → light (FR-015-4 baseline)

    // Marker set post-load: survives only if the page never reloads (a navigation wipes window state).
    await setWindowFlag(page, '__themeParityNoReloadMarker')

    // OS switches to dark mid-session — T-015-02's listener must re-resolve and flip the attribute live.
    // page.emulateMedia updates emulation in place: no navigation, matchMedia fires its change event.
    await page.emulateMedia({ colorScheme: 'dark' })
    await page.waitForFunction(() => document.documentElement.getAttribute('data-theme') === 'dark', null, { timeout: 10_000 })

    // No reload happened — a navigation would wipe this marker (addInitScript doesn't survive it).
    const stillThere = await readWindowFlag(page, '__themeParityNoReloadMarker')
    expect(stillThere, 'the flip must happen in place via the matchMedia listener — no full reload').toBe(true)
  })
})
