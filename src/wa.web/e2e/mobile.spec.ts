import { createRequire } from 'node:module'

import { devices, expect, test } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'

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
 */

// CJS interop with the SDK's Node build — e2e/ sits outside tsc -b (same convention as global-setup.ts).
const require = createRequire(import.meta.url)
const storage = require('@azure/storage-blob')

/** Local stack constants (docker-compose.local.yml + launchSettings Local profile). */
const AZURITE_ACCOUNT_URL = 'http://127.0.0.1:10000/devstoreaccount1'
const AZURITE_ACCOUNT_NAME = 'devstoreaccount1'
const AZURITE_ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
/** API base — override via E2E_API_URL when the stack runs on a non-default port. */
const API_BASE = process.env.E2E_API_URL ?? 'http://localhost:8080'

// ---- Transfer seeding via the real API (TA-4.2#1/#2/#3 wire shapes — same as SendEndpointTests) ----

interface SeededTransfer { linkId: string; files: Array<{ fileId: string; name: string }> }

const blobService = new storage.BlobServiceClient(
  AZURITE_ACCOUNT_URL,
  new storage.StorageSharedKeyCredential(AzuriteAccountName(), AZURITE_ACCOUNT_KEY_B64),
)

/** Local Azurite account name — same as global-setup.ts (no config file at the dev port). */
function AzuriteAccountName(): string { return AZURITE_ACCOUNT_NAME }

/**
 * Seeds an ACTIVE transfer via draft → blob PUT → finalize → send and returns its linkId + files.
 * Staged blobs are written directly to Azurite (TA-3.5 staging path) so the server-side blob copy on
 * finalize succeeds; no browser upload leg is involved.
 */
async function seedTransfer(
  request: APIRequestContext,
  opts: { files: Array<{ name: string; sizeBytes: number; contentType: string }>; password?: string },
): Promise<SeededTransfer> {
  // 1. Draft (TA-4.2#1 exact wire shape).
  const draftRes = await request.post(`${API_BASE}/api/v1/transfers/draft`, { data: { files: opts.files } })
  if (draftRes.status() !== 201) throw new Error(`seed: draft failed — ${draftRes.status()} ${await draftRes.text()}`)
  const draft = await draftRes.json()

  // 2. Stage the blobs (BlobPaths.StagingFile(draftId, fileId) — "n" format hex, no dashes).
  const draftIdN = String(draft.draftId).replace(/-/g, '')
  for (let i = 0; i < opts.files.length; i++) {
    const fileIdN = String(draft.files[i].fileId).replace(/-/g, '')
    const blobPath = `staging/${draftIdN}/f/${fileIdN}`
    const bytes = new Uint8Array(opts.files[i].sizeBytes) // content irrelevant — finalize reads metadata only
    const containerName = blobPath.slice(0, blobPath.indexOf('/'))
    const blobName = blobPath.slice(blobPath.indexOf('/') + 1)
    await blobService.getBlobContainerClient(containerName).getBlobClient(blobName).upload(bytes, bytes.byteLength)
  }

  // 3. Finalize (TA-4.2#2 exact wire shape — fileIds echoed from the draft response; idempotency key).
  const finalizeRes = await request.post(`${API_BASE}/api/v1/transfers/draft/${draft.draftId}/finalize`, {
    data: { files: opts.files.map((f, i) => ({ fileId: draft.files[i].fileId, ...f, retries: 0 })) },
    headers: { 'Idempotency-Key': crypto.randomUUID() },
  })
  if (finalizeRes.status() !== 201) throw new Error(`seed: finalize failed — ${finalizeRes.status()} ${await finalizeRes.text()}`)
  const transfer = await finalizeRes.json()

  // 4. Send (TA-4.2#3 — link-only for the active tests; password set for the gated test).
  const sendRes = await request.post(`${API_BASE}/api/v1/transfers/${transfer.id}/send`, {
    data: { emails: [], ...(opts.password !== undefined ? { password: opts.password } : {}) },
    headers: { 'Idempotency-Key': crypto.randomUUID() },
  })
  if (sendRes.status() !== 201) throw new Error(`seed: send failed — ${sendRes.status()} ${await sendRes.status()}`)

  const sent = await sendRes.json()
  return {
    linkId: String(sent.linkId),
    files: transfer.files.map((f: { id: string; name: string }) => ({ fileId: f.id, name: f.name })),
  }
}

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

// ---- Helpers (deliberately local; e2e/ has no shared seeding module yet — T-003-09 will factor it out) ----
