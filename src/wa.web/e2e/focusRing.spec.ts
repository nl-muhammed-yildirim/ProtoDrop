import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

import { makeSparseFiles } from './helpers'
import { seedTransfer } from './seed-transfer'

/**
 * T-016-05 (F-TRF-016 / AC-016-3 + FR-016-4/5): the single global :focus-visible ring, asserted as a computed
 * box-model property on every P0 surface — landing/drop zone, staging list, link screen (the gap no per-component
 * rule covered), auth/account, recipient password gate. Plus: mouse focus leaves NO ring (:focus-visible only) and
 * prefers-reduced-motion kills the shake while its role=alert fallback text stays visible. Real stack; serial file —
 * one seeded transfer at a time mirrors keyboard.spec.ts's harness (same seed helpers, no new deps).
 */

/** Assert the computed focus ring on a focused element: 2px solid, --accent color, 2px offset. */
async function expectRing(page: Page, selector: string) {
  const info = await page.evaluate((sel) => {
    const el = document.querySelector(sel) as HTMLElement | null
    if (!el) return null
    el.focus() // Chromium treats script focus as keyboard for :focus-visible (no prior mouse gesture in a fresh test).
    const cs = getComputedStyle(el)
    const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()
    return { outlineWidth: cs.outlineWidth, outlineStyle: cs.outlineStyle, outlineOffset: cs.outlineOffset, outlineColor: cs.outlineColor, accent }
  }, selector)
  expect(info, `no element matching ${selector}`).not.toBeNull()
  // hex → rgb normalization for the color compare (tokens are #RRGGBB in both themes).
  const a = info!.accent.replace('#', '')
  const expectedRgb = `rgb(${parseInt(a.slice(0,2),16)}, ${parseInt(a.slice(2,4),16)}, ${parseInt(a.slice(4,6),16)})`
  expect(info!.outlineWidth).toBe('2px')
  expect(info!.outlineStyle).toBe('solid')
  expect(info!.outlineOffset).toBe('2px')
  expect(info!.outlineColor).toBe(expectedRgb)
}

test.describe.configure({ mode: 'serial' })

// ---- Test 1: landing ring + both themes (proves the rule is token-driven, not hardcoded). ----
test('AC-016-3 — drop zone has a 2px solid --accent ring in light AND dark theme', async ({ page }) => {
  await page.goto('/')
  const dz = page.locator('.dz-button')
  await expect(dz).toBeVisible()

  // Light: the pre-paint script resolves data-theme before first paint; assert against whatever token is live.
  await expectRing(page, '.dz-button')

  // Dark: flip the attribute (the app's own mechanism — T-015-02) and re-read. The helper recomputes --accent from
  // the live token, so a hardcoded color would fail here while the token-driven rule passes both themes.
  await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'))
  await expectRing(page, '.dz-button')
})

// ---- Test 2: staging ring (rows + Send). ----
test('AC-016-3 — staged rows and Send have the global ring after seeding one file', async ({ page }) => {
  const fixtures = makeSparseFiles([{ name: 'fring-staged.txt', sizeBytes: 512 }])

  try {
    await page.goto('/')
    // Programmatic seed (keyboard.spec.ts pattern) — no mouse gesture, so script focus stays keyboard-classified.
    await page.setInputFiles('input[type="file"]', fixtures.paths)
    await expect(page.locator('.sl-row')).toHaveCount(1)

    await expectRing(page, '.sl-remove') // the row's remove button (the per-component .sl-* block is deleted).
    await expectRing(page, '.sl-send')
  } finally {
    fixtures.cleanup()
  }
})

// ---- Test 3: link-screen ring — AC-016-3's gap. No .ls-* rule existed before this task; the global one must cover it. ----
test('AC-016-3 — ready-state link screen fields (recipients, copy, send) have the global ring', async ({ page }) => {
  const fixtures = makeSparseFiles([{ name: 'fring-link.txt', sizeBytes: 512 }])

  try {
    await page.goto('/')
    await page.setInputFiles('input[type="file"]', fixtures.paths)
    await expect(page.locator('.sl-row')).toHaveCount(1)

    // Send via keyboard (Enter on the focused button) — keeps this session mouse-free so :focus-visible holds.
    const send = page.locator('.sl-send')
    await expect(send).toBeEnabled()
    await send.press('Enter')

    // Upload completion + ready state (same wait as keyboard.spec.ts's D-7 handoff).
    await expect(page.locator('.ls-heading')).toContainText(/Your link is ready/, { timeout: 60_000 })
    const copyBtn = page.locator('.ls-copy-btn')
    await expect(copyBtn).toHaveCount(1)

    // The three controls the walkthrough set names for this surface — none had a per-component rule before.
    await expectRing(page, '.ls-recipients-textarea')
    await expectRing(page, '.ls-copy-btn')
    await expectRing(page, '.ls-send-btn')
  } finally {
    fixtures.cleanup()
  }
})

// ---- Test 4: auth/account ring (ids grounded in AuthPage.tsx L206/220 — #auth-email / #auth-password). ----
test('AC-016-3 — account page email + password inputs have the global ring', async ({ page }) => {
  await page.goto('/account') // real path (authRoute.ts /^\/account(\/.*)?$/)

  const email = page.locator('#auth-email')
  await expect(email).toBeVisible()
  await expectRing(page, '#auth-email')
  await expectRing(page, '#auth-password')
})

// ---- Test 5: recipient password-gate ring (seeded via API — no browser upload leg). ----
test('AC-016-3 — gated transfer password input/toggle/submit have the global ring', async ({ page, request }) => {
  const seeded = await seedTransfer(request, {
    files: [{ name: 'a.txt', sizeBytes: 128, contentType: 'text/plain' }],
    password: 'hunter2', // gates it (T-003-04) — the gate is what renders these three controls.
  })

  await page.goto(`/t/${seeded.linkId}`)
  const card = page.locator('.rp-password-card')
  await expect(card).toBeVisible()

  await expectRing(page, '.rp-password-input') // per-component .rp-* blocks deleted — global rule must cover them.
  await expectRing(page, '.rp-password-toggle')
  await expectRing(page, '.rp-password-submit')
})

// ---- Test 6: mouse focus leaves NO ring (the universal rule is :focus-visible only, never bare :focus). ----
test('AC-016-3 — clicking the drop zone button does not paint a ring', async ({ page }) => {
  await page.goto('/')

  // The click opens the native file chooser via the hidden input's .click() — cancel it so no staging happens.
  const [fc] = await Promise.all([page.waitForEvent('filechooser'), page.locator('.dz-button').click()])
  await fc.cancel()

  const outlineStyle = await page.evaluate(() => getComputedStyle(document.querySelector('.dz-button')!).outlineStyle)
  expect(outlineStyle, 'a mouse gesture must not trigger :focus-visible — no ring after click').toBe('none')
})

// ---- Test 7: reduced motion (FR-016-5) — shake vanishes under emulation; the role=alert text is fallback-safe. ----
test('FR-016-5 — wrong password under prefers-reduced-motion: no animation, alert text still visible', async ({ page, request }) => {
  const seeded = await seedTransfer(request, {
    files: [{ name: 'a.txt', sizeBytes: 128, contentType: 'text/plain' }],
    password: 'hunter2',
  })

  // Emulate BEFORE navigation so the @media (prefers-reduced-motion) query resolves at load — the .rp-shake rule
  // sits inside a no-preference block and must not apply.
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto(`/t/${seeded.linkId}`)

  const input = page.locator('#rp-password-input')
  await expect(input).toBeVisible()
  await input.fill('wrong-not-hunter2') // wrong → the store sets unlockError, re-arming .rp-shake on the row.
  await input.press('Enter') // form onSubmit — no mouse needed here (no ring assertion follows in this test).

  const alert = page.locator('.rp-password-error[role="alert"]')
  await expect(alert).toContainText(/Wrong password/, { timeout: 10_000 }) // fallback-safe text survives the motion kill.

  // The shake class is present (the component still marks it) but computed animation-name must be 'none'.
  const row = page.locator('.rp-password-row.rp-shake')
  await expect(row).toHaveCount(1, 'shake class should still be applied by the component under reduced motion')
  const animName = await row.evaluate((el) => getComputedStyle(el as HTMLElement).animationName)
  expect(animName, 'no-preference gate must strip the animation under prefers-reduced-motion: reduce').toBe('none')
})
