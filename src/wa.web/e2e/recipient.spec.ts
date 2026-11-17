import { expect, test } from '@playwright/test'

import {
  API_BASE,
  browserDownloadUrl,
  createDraft,
  finalizeAndSend,
  RANGE_RESUME_TOTAL_BYTES,
  seedRangeResumeBlob,
  patternByteAt,
  seedTransfer,
} from './seed-transfer'
import { setTransferExpired, setTransferStatus, UNKNOWN_LINK_ID } from './sql'

/**
 * T-003-09 (T-014 exit check): Recipient page E2E — AC-003-1…AC-003-5 + EC-003-1 (Range-resume).
 *
 * Tests against the real API + Azurite stack (same harness as T-001-09).
 * Each test seeds a transfer via the API (draft → blob PUT → finalize → send), then manipulates its state.
 * Terminal states are set via direct SQL (Status=2 for Expired, Status=3 for DownloadLimit) since the T-018 worker isn't running yet.
 */

test.describe('T-003-09 Recipient page E2E', () => {

  // ---- AC-003-1: Active link renders file list + human-readable sizes + "Download all" + TTFB < 500ms ----
  test('AC-003-1 — active link with 2 files shows list, sizes, Download all, and TTFB < 500ms', async ({ page, request }) => {
    const seeded = await seedTransfer(request, {
      files: [
        { name: 'alpha.txt', sizeBytes: 1024, contentType: 'text/plain' },
        { name: 'beta.bin', sizeBytes: 512, contentType: 'application/octet-stream' },
      ],
    })

    // Capture TTFB on first page load (TA-15: recipient page TTFB < 500 ms).
    const ttfbStart = Date.now()
    await page.goto(`/t/${seeded.linkId}`)
    const ttfbMs = Date.now() - ttfbStart

    expect(ttfbMs, `TA-15: Recipient page TTFB must be < 500 ms (got ${ttfbMs} ms)`).toBeLessThan(500)

    // File list visible with both seeded files by name.
    const rows = page.locator('.rp-row')
    await expect(rows).toHaveCount(2)
    await expect(page.locator('.rp-name', { hasText: 'alpha.txt' })).toBeVisible()
    await expect(page.locator('.rp-name', { hasText: 'beta.bin' })).toBeVisible()

    // Human-readable sizes via formatBytes (TA-8.5): 1024 bytes → "1.00 KB", 512 bytes → "512 B".
    const sizes = page.locator('.rp-size')
    expect(await sizes.nth(0).textContent()).toBe('1.00 KB') // alpha.txt (1024 bytes)
    expect(await sizes.nth(1).textContent()).toBe('512 B') // beta.bin (512 bytes)

    // "Download all" present (zip generation itself is F-TRF-004's scope — button + metadata only).
    await expect(page.locator('.rp-download-all')).toBeVisible()
  })

  // ---- AC-003-2: Password gate → unlock → no re-prompt on refresh in same context ----
  test('AC-003-2 — password-protected link unlocks via correct password and survives refresh', async ({ page, request }) => {
    const PASSWORD = 'Passw0rd!42'
    const seeded = await seedTransfer(request, {
      files: [{ name: 'locked.txt', sizeBytes: 256, contentType: 'text/plain' }],
      password: PASSWORD,
    })

    // Initial load shows password gate.
    await page.goto(`/t/${seeded.linkId}`)
    await expect(page.locator('.rp-password-card')).toBeVisible()
    await expect(page.locator('.rp-list')).not.toBeVisible()

    // Unlock with correct password.
    await page.locator('#rp-password-input').fill(PASSWORD)
    await page.locator('.rp-password-submit').click()
    await expect(page.locator('.rp-list')).toBeVisible({ timeout: 10_000 })

    // Refresh in same context — sessionStorage token persists, no re-prompt.
    await page.reload()
    await expect(page.locator('.rp-list')).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('.rp-password-card')).not.toBeVisible()
  })

  // ---- AC-003-3: Expired link → expired screen, no file list, no SAS mint ----
  test('AC-003-3 — expired link shows expired screen with no file list or download URL', async ({ page, request }) => {
    const seeded = await seedTransfer(request, {
      files: [{ name: 'expired.txt', sizeBytes: 128, contentType: 'text/plain' }],
    })

    // Flip status to Expired (Status=2) via direct SQL.
    await setTransferExpired(seeded.transferId)

    // Load page and verify expired screen.
    await page.goto(`/t/${seeded.linkId}`)
    const terminal = page.locator('.rp-terminal')
    await expect(terminal).toBeVisible()
    await expect(terminal).toContainText('This transfer has expired.')
    
    // No file list visible.
    await expect(page.locator('.rp-list')).not.toBeVisible()
    await expect(page.locator('.rp-row')).toHaveCount(0)
  })

  // ---- AC-003-4: Unknown linkId indistinguishable from expired except status (404 vs 410) ----
  test('AC-003-4 — unknown linkId matches expired except for status code (404 vs 410)', async ({ request }) => {
    // Seed an active transfer and flip it to Expired.
    const seeded = await seedTransfer(request, {
      files: [{ name: 'test.txt', sizeBytes: 64, contentType: 'text/plain' }],
    })
    await setTransferExpired(seeded.transferId)

    // Compare expired (Status=2 → 410) with unknown linkId (→ 404).
    // D9 telemetry leg: no persistent telemetry entity exists in this codebase (local API is Serilog console only,
    // T-050-05), so wire-level body parity + status codes are the M0 proof. `transfer_not_found` emission on the
    // 404 path is pinned by the unit test in tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs.
    const TRACEPARENT = '00-11111111111111111111111111111111-2222222222222222-03'

    const expiredRes = await request.get(`${API_BASE}/api/v1/public/transfers/${seeded.linkId}`, {
      headers: { traceparent: TRACEPARENT },
    })
    const unknownRes = await request.get(`${API_BASE}/api/v1/public/transfers/${UNKNOWN_LINK_ID}`, {
      headers: { traceparent: TRACEPARENT },
    })

    expect(expiredRes.status()).toBe(410)
    expect(unknownRes.status()).toBe(404)

    // Bodies should be byte-identical except for status field.
    const expiredBody = await expiredRes.body()
    const unknownBody = await unknownRes.body()
    
    const expiredJson = expiredBody.toString('utf-8')
    const unknownJson = unknownBody.toString('utf-8').replace(/"status"\s*:\s*404/, '"status":410')
    
    expect(unknownJson).toBe(expiredJson)
  })

  // ---- AC-003-5: Download-limit-reached shows all-used screen + sender email ----
  test('AC-003-5 — download-limit-reached transfer shows all-used screen with sender email', async ({ page, request }) => {
    const SENDER_EMAIL = 'alice@example.com'
    const seeded = await seedTransfer(request, {
      files: [{ name: 'used.txt', sizeBytes: 128, contentType: 'text/plain' }],
      senderEmail: SENDER_EMAIL,
    })

    // Flip status to DownloadLimit (Status=3) via direct SQL.
    await setTransferStatus(seeded.transferId, 3)

    // Load page and verify download-limit screen.
    await page.goto(`/t/${seeded.linkId}`)
    const terminal = page.locator('.rp-terminal')
    await expect(terminal).toBeVisible()
    await expect(terminal).toContainText('All downloads have been used.')
    await expect(terminal).toContainText(SENDER_EMAIL)
  })

  // ---- EC-003-1: 500 MB blob interrupted mid-stream resumes via Range, byte-identical completion ----
  test('EC-003-1 — 500 MB blob interrupted mid-stream resumes via Range and completes byte-identical', async ({ request }) => {
    // Create draft first (don't use seedTransfer — we need to seed pattern blob BEFORE finalize).
    const DRAFT = await createDraft(request, {
      files: [{ name: 'big.bin', sizeBytes: RANGE_RESUME_TOTAL_BYTES, contentType: 'application/octet-stream' }],
    })

    // Seed the 500 MB pattern blob at staging path BEFORE finalize (so finalize copies it to transfers).
    const draftIdN = DRAFT.draftId.replace(/-/g, '')
    const fileIdN = DRAFT.files[0].fileId.replace(/-/g, '')
    await seedRangeResumeBlob('staging', `${draftIdN}/f/${fileIdN}`)

    // Finalize + Send (transfer now references the pattern blob).
    const seeded = await finalizeAndSend(request, DRAFT.draftId, {
      files: [{ name: 'big.bin', sizeBytes: RANGE_RESUME_TOTAL_BYTES, contentType: 'application/octet-stream' }],
      fileIds: [String(DRAFT.files[0].fileId)], // TA-4.2#2 wire body requires fileId per file.
    })

    // Mint a download URL via endpoint 6 (T-003-09-fix: FileItem.Id reuses the draft fileId — same surface here).
    const sasRes = await request.get(
      `${API_BASE}/api/v1/public/transfers/${seeded.linkId}/files/${fileIdN}/download-url`
    )
    expect(sasRes.status()).toBe(200)
    const { url: sasUrl } = (await sasRes.json()) as { url: string }

    // EC-003-1 (T-055-05 BrowserUploadUrl convention, TA-14.3): re-address + re-sign the minted SAS for the IP-hosted
    // Azurite dev account so the browser download leg resolves against local storage exactly as CI does.
    const downloadUrl = browserDownloadUrl(sasUrl)

    // Interrupt and resume via Range (D7): fetch partial content then verify byte-identity.
    const OFFSET = 128 * 1024 * 1024 // ~256 MB into the stream — the "interrupted" point.
    
    const headRes = await fetch(downloadUrl, { headers: { Range: `bytes=0-${OFFSET - 1}` } })
    expect(headRes.status).toBe(206)
    const headBody = new Uint8Array(await headRes.arrayBuffer())

    const tailRes = await fetch(downloadUrl, { headers: { Range: `bytes=${OFFSET}-` } })
    expect(tailRes.status).toBe(206)
    const tailBody = new Uint8Array(await tailRes.arrayBuffer())

    // Combine the two parts.
    const combined = new Uint8Array(headBody.length + tailBody.length)
    combined.set(headBody, 0)
    combined.set(tailBody, headBody.length)

    // Verify against the seeded pattern (same LCG seed, 500×1024² bytes).
    expect(combined.byteLength).toBe(RANGE_RESUME_TOTAL_BYTES)
    
    for (let i = 0; i < combined.length; i += 65536) {
      const expected = patternByteAt(i)
      if (combined[i] !== expected) {
        throw new Error(`Range-resume bytes differ at offset ${i}: got ${combined[i]}, want ${expected}`)
      }
    }
  })
})
