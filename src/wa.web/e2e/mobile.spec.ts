import { devices, expect, test } from '@playwright/test'

import { seedTransfer } from './seed-transfer'

/**
 * T-003-08 (FR-003-10): mobile layout — single column at ≤640 px + sticky Download bar.
 * iPhone profile pinned for the media queries; viewport widened to 780 px tall so the gate card / file list
 * fit one screen for the "reachable without scrolling" assertions (360 px wide — the FR-003-10 exit-check width).
 *
 * Transfers are seeded via the real API (draft → blob PUT → finalize → send), NOT through the UI upload flow:
 * HEAD has no client-side link screen mounted yet (T-003-09 owns that wiring — flagged as an open question).
 * Blob PUTs use @azure/storage-blob against local Azurite (same credential style as global-setup.ts).
 * The API base is overridable via E2E_API_URL so a later run can point at a different port.
 *
 * Status at commit: WRITTEN BUT UNRUN — port 8080 held by SABnzbd (personal download manager), API unreachable.
 * T-003-09 refactor: the seed helper now lives in './seed-transfer' (shared with recipient.spec.ts).
 */

// ---- Mobile context (iPhone profile, plan-pinned viewport) ----

test.use({ ...devices['iPhone 13'], viewport: { width: 360, height: 780 }, isMobile: true, hasTouch: true })

// ---- Tests ----

test.describe('T-003-08 mobile sticky bar (FR-003-10)', { mode: 'serial' }, () => {

  /** Exit check: at 360 px — single column, no horizontal scroll, sticky "Download all" at the viewport bottom. */
  test('360 px: single column, no horizontal scroll, sticky Download all bar at viewport bottom', async ({ page, request }) => {
    const seeded = await seedTransfer(request, {
      files: [
        { name: 'alpha.txt', sizeBytes: 1024, contentType: 'text/plain' },
        { name: 'beta.bin', sizeBytes: 512, contentType: 'application/octet-stream' },
      ],
    })

    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-list')).toBeVisible()
    await expect(page.locator('.rp-row')).toHaveCount(2)

    // No horizontal scroll at 360 px (document.scrollWidth === window.innerWidth).
    const metrics = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }))
    expect(metrics.sw, 'no horizontal scroll at 360 px').toBeLessThanOrEqual(metrics.iw)

    // Sticky bar present and pinned to the viewport's bottom (tolerant of sub-pixel rounding).
    const stickyBar = page.locator('.rp-sticky')
    await expect(stickyBar).toBeVisible()
    const rect = (await stickyBar.boundingBox()) as { y: number; height: number }
    const viewportHeight = 780
    expect(rect.y + rect.height, 'sticky bar bottom sits at the viewport bottom').toBeCloseTo(viewportHeight, -1)

    // 2 files → hasDownloadAll (server-computed, TA-4.2#4) → the bar's button is "Download all".
    await expect(stickyBar.locator('button')).toHaveText(/Download all/)
  })

  /** Exit check (per-file): a row Download tap mints endpoint 6 and downloads the ORIGINAL filename. */
  test('row Download tap streams the blob with the original filename (endpoint 6, T-003-06 path)', async ({ page, request }) => {
    const seeded = await seedTransfer(request, {
      files: [{ name: 'original-name α.txt', sizeBytes: 512, contentType: 'text/plain' }],
    })

    // Capture the browser's blob GET (Content-Disposition attachment navigation against storage).
    const blobGetPromise = page.waitForEvent('response', async (res) => res.url().includes('?sv='))

    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-row')).toBeVisible()
    // The single-file transfer's row button label follows the exact exit-check format.
    const rowBtn = page.locator('.rp-row').locator('button[aria-label^="Download"]')
    await expect(rowBtn).toHaveAttribute('aria-label', `Download ${seeded.files[0].name}, 512 B`)
    await rowBtn.click()

    const blobRes = await blobGetPromise
    expect(blobRes.status()).toBe(200)
    // Original filename survives via the RFC 5987 attachment disposition minted server-side (EC-003-3).
    const disposition = blobRes.headers()['content-disposition'] ?? ''
    expect(disposition).toContain(`UTF-8''${encodeURIComponent(seeded.files[0].name)}`)
  })

  /** Exit check (password flow): gated link → gate card + input in viewport; unlock succeeds, list renders. */
  test('gated transfer: password card reachable without scrolling, unlock reveals file list', async ({ page, request }) => {
    const PASSWORD = 'Passw0rd!42'
    const seeded = await seedTransfer(request, {
      files: [{ name: 'locked.txt', sizeBytes: 256, contentType: 'text/plain' }],
      password: PASSWORD,
    })

    await page.goto(`/t/${seeded.linkId}`)

    // Gated screen — the gate card is the ONLY content (T-003-05 byte-leak rule).
    const gate = page.locator('.rp-password-card')
    await expect(gate).toBeVisible()
    const input = page.locator('#rp-password-input')
    await expect(input).toBeVisible()
    // The input + Unlock button both fit inside the viewport — nothing to scroll past (plan exit check).
    const inputRect = (await input.boundingBox()) as { y: number; height: number }
    const submitRect = (await page.locator('.rp-password-submit').boundingBox()) as { y: number; height: number }
    const viewportHeight = 780
    expect(inputRect.y + inputRect.height, 'password input below the fold?').toBeLessThan(viewportHeight)
    expect(submitRect.y + submitRect.height, 'Unlock button below the fold?').toBeLessThan(viewportHeight)

    // Unlock — endpoint 5 → token in sessionStorage → endpoint 4 re-query with ?t= → active file list.
    await input.fill(PASSWORD)
    await page.locator('.rp-password-submit').click()
    await expect(page.locator('.rp-list')).toBeVisible({ timeout: 5000 })
  })

})
