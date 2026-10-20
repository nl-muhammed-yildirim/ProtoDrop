import type { APIRequestContext } from '@playwright/test'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { Readable } from 'node:stream'

/**
 * T-003-09 (T-014 exit check): transfer seeding via the real API (draft → blob PUT → finalize → send),
 * extracted from mobile.spec.ts so the recipient specs stay focused on assertions. TA-4.2#1/#2/#3 wire shapes
 * are pinned by SendEndpointTests; this is the shared harness path for AC-003-1…5 + EC-003-1 (T-014), reusing
 * the T-001-09 conventions (same stack, same credential style as global-setup.ts — no new test setup).
 */

// CJS interop with the SDK's Node build — e2e/ sits outside tsc -b (same convention as global-setup.ts).
const require = createRequire(import.meta.url)
const storage = require('@azure/storage-blob')

/** Local stack constants (docker-compose.local.yml + launchSettings Local profile — same as global-setup.ts). */
export const AZURITE_ACCOUNT_URL = 'http://127.0.0.1:10000/devstoreaccount1'
const AZURITE_ACCOUNT_NAME = 'devstoreaccount1'
const AZURITE_ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
/** API base — override via E2E_API_URL when the stack runs on a non-default port (T-003-08 convention). */
export const API_BASE = process.env.E2E_API_URL ?? 'http://localhost:8080'

// Singleton shared by every seeding call in this worker (workers: 1 — playwright.config.ts).
const blobService = new storage.BlobServiceClient(
  AZURITE_ACCOUNT_URL,
  new storage.StorageSharedKeyCredential(AZURITE_ACCOUNT_NAME, AZURITE_ACCOUNT_KEY_B64),
)

export interface SeededTransfer {
  /** 8-char link id (TA-9.6 charset) — the /t/{linkId} route segment. */
  linkId: string
  /** Transfer Id in "n" format (no dashes). */
  transferId: string
  files: Array<{ fileId: string; name: string }>
  totalBytes: number
}

interface SeedOptions {
  files: Array<{ name: string; sizeBytes: number; contentType: string }>
  password?: string
  senderEmail?: string
}

// ---- Step 1: Draft (TA-4.2#1) ----

interface DraftResponse {
  draftId: string // "n" format
  files: Array<{ fileId: string; name: string }> // "n" format fileIds
}

/** Step 1: create a draft via the API. */
export async function createDraft(
  request: APIRequestContext,
  opts: SeedOptions,
): Promise<DraftResponse> {
  const draftRes = await request.post(`${API_BASE}/api/v1/transfers/draft`, { data: { files: opts.files } })
  if (draftRes.status() !== 201) throw new Error(`seed: draft failed — ${draftRes.status()} ${await draftRes.text()}`)
  return (await draftRes.json()) as DraftResponse
}

/**
 * EC-003-1 (T-014 exit check): re-addresses a minted recipient SAS URL for the IP-hosted Azurite dev account and re-signs
 * it over the same blob-SAS string-to-sign with the protocol line changed to "https,http" — the exact convention of
 * SasRoundTripTests.BrowserUploadUrl (T-055-05, TA-14.3). The canonical name signed by the minter is
 * /blob/devstoreaccount1/transfers/…; the minted URL drops the account path segment for an IP host, so it must be
 * re-inserted to match that signature. MintRecipientFile signs resp-content-disposition into the string-to-sign,
 * so rscd is carried over (element 13 of the 16-line blob-SAS canonical form).
 */
export function browserDownloadUrl(mintedUrl: string): string {
  const minted = new URL(mintedUrl)
  const q = minted.searchParams
  const sp = q.get('sp') ?? 'r'
  const st = q.get('st') ?? ''
  const se = q.get('se') ?? ''
  const sv = q.get('sv') ?? '2024'
  const sr = q.get('sr') ?? 'b'
  const rscd = q.get('rscd') ?? ''

  // Blob-SAS canonical string-to-sign (16 lines): sp, st, se, canonical name, identifier, iprange, protocol,
  // sv, sr, snapshot, encryption scope, cache-control, content-disposition, encoding, language, content-type.
  const lines = [sp, st, se, `/blob/${AZURITE_ACCOUNT_NAME}${minted.pathname}`, '', '', 'https,http', '2024', sr, '', '', '', rscd, '', '', '']
  const sig = crypto.createHmac('sha256', Buffer.from(AZURITE_ACCOUNT_KEY_B64, 'base64')).update(lines.join('\n')).digest('base64')

  return `http://127.0.0.1:10000/${AZURITE_ACCOUNT_NAME}${minted.pathname}?sv=${sv}&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=${sp}&rscd=${encodeURIComponent(rscd)}&sig=${encodeURIComponent(sig)}`
}

// ---- Step 3+4: Finalize + Send (TA-4.2#2/#3) ----

/** Steps 3+4: finalize + send the transfer. Caller must have already uploaded blobs at the staging path.
 * `fileIds` comes from the draft response — the TA-4.2#2 wire body requires fileId per file (Program.cs parses it). */
export async function finalizeAndSend(
  request: APIRequestContext,
  draftId: string,
  opts: SeedOptions & { fileIds: Array<string> },
): Promise<SeededTransfer> {
  const finalizeRes = await request.post(`${API_BASE}/api/v1/transfers/draft/${draftId}/finalize`, {
    data: { files: opts.files.map((f, i) => ({ ...f, fileId: opts.fileIds[i], retries: 0 })) },
    headers: { 'Idempotency-Key': crypto.randomUUID() },
  })
  if (finalizeRes.status() !== 201) throw new Error(`seed: finalize failed — ${finalizeRes.status()} ${await finalizeRes.text()}`)
  const transfer = await finalizeRes.json()

  const sendBody: Record<string, unknown> = { emails: [] as string[] }
  if (opts.password !== undefined) sendBody.password = opts.password
  if (opts.senderEmail !== undefined) sendBody.senderEmail = opts.senderEmail
  const sendRes = await request.post(`${API_BASE}/api/v1/transfers/${transfer.id}/send`, {
    data: sendBody,
    headers: { 'Idempotency-Key': crypto.randomUUID() },
  })
  if (sendRes.status() !== 201) throw new Error(`seed: send failed — ${sendRes.status()} ${await sendRes.text()}`)

  const sent = await sendRes.json()
  return {
    linkId: String(sent.linkId),
    transferId: String(transfer.id).replace(/-/g, ''),
    files: (transfer.files as Array<{ id: string; name: string }>).map((f) => ({ fileId: f.id.replace(/-/g, ''), name: f.name })),
    totalBytes: Number(sent.totalBytes),
  }
}

// ---- Step 2: Upload blobs at the staging path (TA-3.5) ----

/** Step 2 (small blobs): upload zero-filled staging blobs for all files. */
async function stageSmallStagingBlobs(draft: DraftResponse, opts: SeedOptions): Promise<void> {
  const draftIdN = String(draft.draftId).replace(/-/g, '')
  const containerClient = blobService.getContainerClient('staging')
  for (let i = 0; i < opts.files.length; i++) {
    const fileIdN = String(draft.files[i].fileId).replace(/-/g, '') // fileIds come from the draft response (T-001-09 convention).
    const bytes = new Uint8Array(opts.files[i].sizeBytes) // content irrelevant — finalize reads metadata only.
    const blobName = `${draftIdN}/f/${fileIdN}` // BlobPaths.StagingFile pattern (TA-3.5 staging row).
    // 12.24.0: uploads live on the block-blob client (getBlobClient's plain client has no upload method).
    await containerClient.getBlockBlobClient(blobName).upload(bytes, bytes.byteLength)
  }
}

/**
 * Seeds an ACTIVE transfer via draft → blob PUT → finalize → send and returns linkId + files + totalBytes.
 * Staged blobs are written directly to Azurite (TA-3.5 staging path) so the server-side blob copy on finalize
 * succeeds; no browser upload leg is involved. Optional `password` gates it (T-003-04); optional `senderEmail`
 * sets the terminal screen's contact address (AC-003-5).
 */
export async function seedTransfer(
  request: APIRequestContext,
  opts: SeedOptions,
): Promise<SeededTransfer> {
  const draft = await createDraft(request, opts)
  await stageSmallStagingBlobs(draft, opts)
  return finalizeAndSend(request, draft.draftId, { ...opts, fileIds: draft.files.map((f) => String(f.fileId)) })
}

// ---- EC-003-1 (T-014 exit check): 500 MB Range-resume seeding + verification ----

/** 500 MB (EC-003-1). */
export const RANGE_RESUME_TOTAL_BYTES = 500 * 1024 * 1024
/** 1 MiB chunk (T-014 "seeded PRNG" pattern: 500×1024² bytes = 500 chunks × 1 MiB). */
export const RANGE_CHUNK_SIZE = 1024 * 1024

// LCG constants — deterministic byte pattern for the 500 MB blob. The same seed + recurrence on both sides:
// seeding fills bytes from successive states; verification recomputes x_{i+1} via jump-ahead (binary exp).
const LCG_X0 = 0x9e3779b9 | 0 // arbitrary fixed seed — the pattern is position-based, not time-based.
const LCG_A = 1664525n
const LCG_C = 1013904223n
const LCG_MODULUS = 1n << 32n

/** One LCG step with fast 32-bit arithmetic (Math.imul). */
function lcgStepFast(state: number): number {
  return (Math.imul(state, Number(LCG_A)) + Number(LCG_C)) & 0xffffffff
}

/** x_n from x_0 — binary exponentiation of the affine transition (a·x + c) mod 2^32. */
function lcgValueAt(x0: number, n: bigint): number {
  let resA = 1n, resC = 0n // accumulator: identity transition (1, 0).
  let curA = LCG_A, curC = LCG_C // base transition.
  let e = n
  while (e > 0n) {
    if (e & 1n) {
      const nra = (resA * curA) % LCG_MODULUS
      const nrc = ((resA * curC) % LCG_MODULUS + resC) % LCG_MODULUS
      resA = nra; resC = nrc
    }
    const nca = (curA * curA) % LCG_MODULUS
    const ncc = ((curA * curC) % LCG_MODULUS + curC) % LCG_MODULUS
    curA = nca; curC = ncc
    e >>= 1n
  }
  return Number((resA * BigInt(x0) + resC) % LCG_MODULUS)
}

/** Byte at global index i in the pattern stream: (T^(i+1)(X0)) & 0xff — O(log i). */
export function patternByteAt(i: number): number {
  return lcgValueAt(LCG_X0, BigInt(i) + 1n) & 0xff
}

/**
 * Writes a seeded 500 MB block blob to Azurite at the given blob name (within the specified container).
 * The blob is later read back via ranged GETs (EC-003-1 resume) — the seeded fill means the spec can verify
 * byte-identity without storing the bytes anywhere else (same LCG seed across runs).
 *
 * Uses uploadStream with a Readable of 1 MiB chunks: the @azure/storage-blob 12.24.0 stageBlock path
 * serializes Uint8Array/Buffer bodies as JSON arrays via @typespec/ts-http-runtime (ADR row), so only the
 * stream upload surface is binary-safe for large payloads in this stack.
 */
export async function seedRangeResumeBlob(containerName: string, blobName: string): Promise<{ totalBytes: number; chunkSize: number }> {
  const containerClient = blobService.getContainerClient(containerName)
  await containerClient.createIfNotExists() // staging already exists — this is a no-op safeguard.
  const blobClient = containerClient.getBlockBlobClient(blobName)

  const stream = new Readable({
    read() {
      let offset = 0
      let state = LCG_X0 // seeded fill — same recurrence as before the uploadStream refactor.
      while (offset < RANGE_RESUME_TOTAL_BYTES) {
        const chunk = new Uint8Array(RANGE_CHUNK_SIZE)
        for (let i = 0; i < RANGE_CHUNK_SIZE; i++) {
          state = lcgStepFast(state)
          chunk[i] = state & 0xff // byte at global offset (offset + i).
        }
        this.push(chunk)
        offset += RANGE_CHUNK_SIZE
      }
      this.push(null) // EOF.
    },
  })

  // uploadStream(readableBody, blobSize) — chunked block-blob upload handled internally by the SDK.
  await (blobClient as unknown as { uploadStream: (body: Readable, size: number) => Promise<unknown> }).uploadStream(stream, RANGE_RESUME_TOTAL_BYTES)

  return { totalBytes: RANGE_RESUME_TOTAL_BYTES, chunkSize: RANGE_CHUNK_SIZE }
}

/** Verifies the seeded blob's bytes match at (offset, length) via a ranged GET — byte-identical across runs. */
export async function verifySeededBlobRange(blobUrl: string, offset: number, length: number): Promise<void> {
  const res = await fetch(blobUrl, { headers: { Range: `bytes=${offset}-${offset + length - 1}` } })
  if (res.status !== 206) throw new Error(`expected 206 Partial Content, got ${res.status}`)
  const data = new Uint8Array(await res.arrayBuffer())

  for (let i = 0; i < length; i++) {
    const expected = patternByteAt(offset + i) // seeded pattern at this offset.
    if (data[i] !== expected) {
      throw new Error(`byte mismatch at offset ${offset + i}: got ${data[i]}, want ${expected}`)
    }
  }
}
