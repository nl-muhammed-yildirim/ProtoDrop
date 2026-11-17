import { expect, test } from '@playwright/test'
import type { Download, Page } from '@playwright/test'
import fs from 'node:fs'

import { makeSparseFiles } from './helpers'
import {
  buildStoredZip,
  expectPhoneLayout,
  expectStickyClear,
  expectViewportMetaScalable,
  expectZoomSafe,
  seedSignedInSession,
} from './mobile-helpers'
import { seedTransfer, seedTransferZip } from './seed-transfer'

/**
 * T-017-04 (US-017-03 + US-017-01/02): mobile matrix — iPhone 14 (WebKit) and Pixel 7 (Chromium) projects from
 * playwright.config.ts run this file only; the device descriptors (viewport, isMobile, hasTouch) come from the project.
 * Covers AC-017-1 (guest loop), AC-017-2 / FR-017-3 (download-all + per-file, original names), the password flow,
 * and AC-017-3 / EC-017-3 (44 px targets, 16 px inputs, no horizontal scroll, pinch-zoom + 200 % zoom) at 360 px.
 * T-003-08 sticky-bar checks live in the 360 px describe.
 *
 * Transfers are seeded via the real API (draft → blob PUT → finalize → send) and downloads are asserted through
 * Playwright's download event (suggestedFilename). The OS photo picker is replaced by setInputFiles (T-001-09 convention).
 * The suite is local-stack-only (hard-coded API base + Azurite dev key in seed-transfer.ts); T-028 selects the projects by name.
 */

test.describe.configure({ mode: 'serial' })

/** Photo-like fixtures for the AC-017-1 picker stand-in (~200 KB each → one block per file). */
function makePhotos() {
  return makeSparseFiles([
    { name: 'IMG_0001.HEIC', sizeBytes: 200 * 1024 },
    { name: 'IMG_0002.JPG', sizeBytes: 200 * 1024 },
  ])
}

/** Recipient link ids use the TA-9.6 charset (RECIPIENT_PATH_RE). */
const LINK_URL_RE = /\/t\/[0-9a-hjkmnpqrstvwxyz]{8}$/

/**
 * FR-017-3: a per-file download keeps the original name. Chromium asserts suggestedFilename() exactly. Playwright's WebKit
 * build does not decode the RFC 5987 `filename*=UTF-8''…` form into suggestedFilename (it returns the blob id), so there the
 * same contract is asserted on the SAS URL's rscd (response-content-disposition) param — attachment + exact encoded name.
 */
function expectOriginalName(download: Download, name: string, browserName: string): void {
  if (browserName !== 'webkit') {
    expect(download.suggestedFilename()).toBe(name)
    return
  }
  const rscd = new URL(download.url()).searchParams.get('rscd') ?? ''
  expect(rscd).toMatch(/^attachment/i)
  expect(rscd).toContain(`filename*=UTF-8''${encodeURIComponent(name)}`)
}

/** Upload the photos from the landing page and wait for the ready link screen. */
async function uploadPhotosToReady(page: Page, paths: string[]): Promise<void> {
  await page.goto('/')
  await page.setInputFiles('input[type="file"]', paths)
  await expect(page.locator('.sl-row')).toHaveCount(paths.length)
  await page.locator('.sl-send').tap()
  await expect(page.locator('.ls-heading')).toHaveText(/Your link is ready/, { timeout: 60_000 })
}

// ---- AC-017-1: guest loop at the device viewport ----

test.describe('AC-017-1 guest loop (device viewport)', () => {
  test('pick 2 photos → progress → link → Copy', async ({ page, browserName }) => {
    const photos = makePhotos()
    try {
      // Slow the direct-to-Azurite block PUTs a little so progress is observable (preflight OPTIONS pass through).
      await page.route('http://127.0.0.1:10000/**', async (route) => {
        if (route.request().method() === 'PUT') await new Promise((r) => setTimeout(r, 600))
        await route.continue()
      })

      await page.goto('/')
      await expectViewportMetaScalable(page)
      await page.setInputFiles('input[type="file"]', photos.paths)
      await expect(page.locator('.sl-row')).toHaveCount(2)

      const send = page.locator('.sl-footer .sl-send')
      await expect(send).toBeInViewport() // sticky footer (T-017-01) — reachable without scrolling.
      await send.tap()

      await expect(page.locator('.sl-progress-track').first()).toBeVisible()
      // The list unmounts into the link screen as soon as the last row is done, so "done" is asserted by that transition.
      await expect(page.locator('.ls-heading')).toHaveText(/Your link is ready/, { timeout: 60_000 })
      const url = (await page.locator('.ls-url').textContent())?.trim() ?? ''
      expect(url).toMatch(LINK_URL_RE)

      await page.locator('.ls-copy-btn').tap()
      await expect(page.locator('.ls-copy-success[role="status"]')).toContainText(/Copied/)
      if (browserName === 'chromium') {
        // Pixel project grants clipboard permissions; WebKit has no grant — UI state only there.
        expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(url)
      }
    } finally {
      photos.cleanup()
    }
  })
})

// ---- AC-017-2 / FR-017-3: download-all + per-file at the device viewport ----

test.describe('AC-017-2 / FR-017-3 download-all + per-file (device viewport)', () => {
  const ZIP_FILES = [
    { name: 'photo α.jpg', sizeBytes: 2048, contentType: 'image/jpeg' },
    { name: 'notes.txt', sizeBytes: 512, contentType: 'text/plain' },
  ]

  async function seedWithReadyZip(request: Parameters<typeof seedTransfer>[0]) {
    const seeded = await seedTransfer(request, { files: ZIP_FILES })
    const zip = buildStoredZip(ZIP_FILES.map((f) => ({ name: f.name, data: Buffer.alloc(f.sizeBytes) })))
    await seedTransferZip(seeded.transferId, zip)
    return { seeded, zip }
  }

  test('zip ready: Download all tap downloads all.zip, byte-identical', async ({ page, request }) => {
    const { seeded, zip } = await seedWithReadyZip(request)
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-row')).toHaveCount(2)
    await expect(page.locator('.rp-cap-note')).toHaveCount(0)

    const [download] = await Promise.all([page.waitForEvent('download'), page.locator('.rp-download-all').tap()])
    expect(download.suggestedFilename()).toBe('all.zip')
    const saved = fs.readFileSync((await download.path()) as string)
    expect(saved.equals(zip), 'downloaded zip is byte-identical to the stored zip').toBe(true)
  })

  test('cancelled zip download leaves no error state, button back to idle (EC-004-4)', async ({ page, request }) => {
    const { seeded } = await seedWithReadyZip(request)
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-row')).toHaveCount(2)

    const [zipDownload] = await Promise.all([page.waitForEvent('download'), page.locator('.rp-download-all').tap()])
    await zipDownload.cancel()
    await expect(page.locator('.dz-toast')).toHaveCount(0)
    await expect(page.locator('.rp-download-all')).toBeEnabled()
    await expect(page.locator('.rp-download-all')).toHaveText(/Download all/)
    // EC-004-4: per-file fallback stays present and usable after the cancel.
    await expect(page.locator('.rp-row .rp-download')).toHaveCount(2)
    await expect(page.locator('.rp-row .rp-download').first()).toBeEnabled()

    // Per-file Download after a minted zip is covered by the single-file / password tests: local seeded transfers carry
    // MaxDownloads = 1 (guest default), so a second mint on THIS transfer is a cap-reached case, not a recovery check.
  })

  test('zip not ready: Preparing state (spinner, disabled) and per-file Download still works', async ({ page, request, browserName }) => {
    const seeded = await seedTransfer(request, { files: ZIP_FILES }) // no zip blob → endpoint 7 answers 202.
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-row')).toHaveCount(2)

    const all = page.locator('.rp-download-all')
    await all.tap()
    await expect(all).toHaveText(/Preparing your download…/)
    await expect(all).toBeDisabled()
    await expect(all).toHaveAttribute('aria-busy', 'true')
    await expect(page.locator('.rp-download-all-hint')).toBeVisible()

    // Row buttons are disabled only while a mint is in flight (downloadingFileId), not while the zip prepares.
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.locator('.rp-row').first().locator('.rp-download').tap(),
    ])
    expectOriginalName(download, ZIP_FILES[0].name, browserName)
  })

  test('single file: only the row Download, saved with the exact original name', async ({ page, request, browserName }) => {
    const seeded = await seedTransfer(request, {
      files: [{ name: 'original-name α.txt', sizeBytes: 512, contentType: 'text/plain' }],
    })
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-row')).toHaveCount(1)
    await expect(page.locator('.rp-download-all')).toHaveCount(0)
    await expect(page.locator('.rp-cap-note')).toHaveCount(0)

    const rowBtn = page.locator('.rp-row .rp-download')
    await expect(rowBtn).toHaveAttribute('aria-label', `Download ${seeded.files[0].name}, 512 B`)
    const [download] = await Promise.all([page.waitForEvent('download'), rowBtn.tap()])
    expectOriginalName(download, seeded.files[0].name, browserName)
  })
})

// ---- Password flow at the device viewport ----

test.describe('password flow (device viewport)', () => {
  test('gated transfer: gate in viewport, unlock reveals list, row Download keeps the original name', async ({ page, request, browserName }) => {
    const PASSWORD = 'Passw0rd!42'
    const seeded = await seedTransfer(request, {
      files: [{ name: 'locked.txt', sizeBytes: 256, contentType: 'text/plain' }],
      password: PASSWORD,
    })

    await page.goto(`/t/${seeded.linkId}`)

    // Gated screen — the gate card is the ONLY content (T-003-05 byte-leak rule).
    await expect(page.locator('.rp-password-card')).toBeVisible()
    const input = page.locator('#rp-password-input')
    await expect(input).toBeVisible()
    // Input + Unlock both fit inside the viewport — nothing to scroll past.
    const viewportHeight = page.viewportSize()!.height
    const inputRect = (await input.boundingBox()) as { y: number; height: number }
    const submitRect = (await page.locator('.rp-password-submit').boundingBox()) as { y: number; height: number }
    expect(inputRect.y + inputRect.height, 'password input below the fold?').toBeLessThan(viewportHeight)
    expect(submitRect.y + submitRect.height, 'Unlock button below the fold?').toBeLessThan(viewportHeight)

    // Unlock — endpoint 5 → token in sessionStorage → endpoint 4 re-query with ?t= → active file list.
    await input.fill(PASSWORD)
    await page.locator('.rp-password-submit').tap()
    await expect(page.locator('.rp-list')).toBeVisible()

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.locator('.rp-row .rp-download').first().tap(),
    ])
    expectOriginalName(download, 'locked.txt', browserName)
  })
})

// ---- AC-017-3 + EC-017-3 at 360 px (the FR-003-10 / FR-017 phone width) ----

test.describe('AC-017-3 + EC-017-3 at 360 px', () => {
  test.use({ viewport: { width: 360, height: 780 } })

  const TWO_FILES = [
    { name: 'alpha.txt', sizeBytes: 1024, contentType: 'text/plain' },
    { name: 'beta.bin', sizeBytes: 512, contentType: 'application/octet-stream' },
  ]

  /** T-003-08 exit check: single column, no horizontal scroll, sticky Download all at the viewport bottom. */
  test('recipient 2 files: single column, sticky Download all bar at viewport bottom', async ({ page, request }) => {
    const seeded = await seedTransfer(request, { files: TWO_FILES })
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-list')).toBeVisible()
    await expect(page.locator('.rp-row')).toHaveCount(2)

    const stickyBar = page.locator('.rp-download-all')
    await expect(stickyBar).toBeVisible()
    await expect(stickyBar).toHaveText(/Download all/)
    const rect = (await stickyBar.boundingBox()) as { y: number; height: number }
    expect(rect.y + rect.height, 'sticky bar bottom sits at the viewport bottom').toBeCloseTo(page.viewportSize()!.height, -1)

    await expectPhoneLayout(page, 'recipient active (2 files)')
    await expectStickyClear(page, '.rp-row:last-child', '.rp-download-all')
    await expectZoomSafe(page, 'recipient active (2 files)')

    // Preparing state (no zip seeded → 202) keeps the layout contract.
    await stickyBar.tap()
    await expect(stickyBar).toHaveText(/Preparing your download…/)
    await expectPhoneLayout(page, 'recipient preparing')
  })

  test('landing: empty and staged (sticky footer clears the list)', async ({ page }) => {
    const photos = makePhotos()
    try {
      await page.goto('/')
      await expect(page.locator('input[type="file"]')).toBeAttached()
      await expectPhoneLayout(page, 'landing empty')
      await expectZoomSafe(page, 'landing empty')

      await page.setInputFiles('input[type="file"]', photos.paths)
      await expect(page.locator('.sl-row')).toHaveCount(2)
      await expectPhoneLayout(page, 'landing staged')
      await expectStickyClear(page, '.sl-row:last-child', '.sl-footer')
    } finally {
      photos.cleanup()
    }
  })

  test('link screen (ready state)', async ({ page }) => {
    const photos = makePhotos()
    try {
      await uploadPhotosToReady(page, photos.paths)
      await expectPhoneLayout(page, 'link screen ready')
    } finally {
      photos.cleanup()
    }
  })

  test('recipient gate (password) — layout + zoom', async ({ page, request }) => {
    const seeded = await seedTransfer(request, { files: TWO_FILES, password: 'Passw0rd!42' })
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-password-card')).toBeVisible()
    await expectPhoneLayout(page, 'recipient gate')
    await expectZoomSafe(page, 'recipient gate')
  })

  test('recipient terminal: not found', async ({ page }) => {
    await page.goto('/t/zzzzzzzz') // valid charset, unknown id.
    await expect(page.locator('.rp-terminal')).toBeVisible()
    await expectPhoneLayout(page, 'recipient not found')
  })

  test('cap note: ≥2 files without server Download all (T-004-04)', async ({ page, request }) => {
    const seeded = await seedTransfer(request, { files: TWO_FILES })
    // Rewrite ONLY hasDownloadAll on endpoint 4 (seeding GBs to cross MAX_ZIP_SIZE is not viable). Exact path: mints untouched.
    await page.route(`**/api/v1/public/transfers/${seeded.linkId}`, async (route) => {
      const response = await route.fetch()
      const json = (await response.json()) as Record<string, unknown>
      await route.fulfill({ response, json: { ...json, hasDownloadAll: false } })
    })
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-row')).toHaveCount(2)
    const note = page.locator('.rp-cap-note')
    await expect(note).toBeVisible()
    await expect(note).toHaveText('Files are large — download individually.')
    await expect(page.locator('.rp-download-all')).toHaveCount(0)
    const box = (await note.boundingBox()) as { x: number; width: number }
    expect(box.x).toBeGreaterThanOrEqual(0)
    expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width)
    await expectPhoneLayout(page, 'recipient cap note')
  })

  test('auth screen /account', async ({ page }) => {
    await page.goto('/account')
    await expect(page.locator('input').first()).toBeVisible()
    await expectPhoneLayout(page, 'account')
    await expectZoomSafe(page, 'account')
  })

  test('My Files (signed in)', async ({ page, context, request }) => {
    await seedSignedInSession(page, context, request)
    // page.request shares the context cookie jar → the draft endpoint attributes the owner from the session.
    await seedTransfer(page.request, { files: TWO_FILES })
    await page.goto('/files')
    await expect(page.locator('.mf-row').first()).toBeVisible()
    await expectPhoneLayout(page, 'my files')
  })
})
