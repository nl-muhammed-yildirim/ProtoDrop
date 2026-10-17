import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'

import type { Page, Request } from '@playwright/test'

/** T-001-09: the block size is pinned by UploadEngine (TA-8.3) — tests correlate on it, no literals of their own. */
export const BLOCK_SIZE = 8 * 1024 * 1024

/** Free-plan limit in bytes (Part 2 / LimitsRecord seed). AC-001-2 stages just over it. */
export const FREE_MAX_TRANSFER_SIZE_BYTES = 5368_709_120

interface SparseFiles {
  /** Absolute paths, stageable via page.setInputFiles — real files with the right names and sizes. */
  paths: string[]
  /** Removes every created file + directory (idempotent). */
  cleanup(): void
}

/**
 * Creates uniquely-named sparse files at the requested sizes (AC-001-2 needs ~6 GiB of metadata, not disk):
 * an 8 KiB header write plus one byte at sizeBytes−1 leaves the middle unwritten.
 */
export function makeSparseFiles(specs: Array<{ name: string; sizeBytes: number }>): SparseFiles {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'protodrop-e2e-'))
  const paths = specs.map((spec) => {
    const p = path.join(dir, spec.name)
    fs.writeFileSync(p, Buffer.alloc(8192))
    if (spec.sizeBytes > 0) {
      const fd = fs.openSync(p, 'r+') // r+, not a+: under O_APPEND the offset overload is ignored (file ended up 8 KiB + 1 byte).
      try {
        fs.writeSync(fd, Buffer.from([0]), 0, 1, spec.sizeBytes - 1) // seek to size−1 + one zero byte: sparse in between.
      } finally {
        fs.closeSync(fd)
      }
    } else {
      fs.truncateSync(p, 0)
    }
    return p
  })
  return {
    paths,
    cleanup: () => {
      try {
        for (const p of paths) if (fs.existsSync(p)) fs.unlinkSync(p)
        fs.rmSync(dir, { recursive: true, force: true })
      } catch { /* best effort — a leftover temp file is harmless */ }
    },
  }
}

/** Draft wire shape the web's Send handler consumes (T-001-05). */
export interface MintedDraftFile { fileId: string; uploadUrl: string }
export interface MintedDraft { draftId: string; expiresInSec: number; files: MintedDraftFile[] }

/**
 * Resolves with the minted blob URLs from POST /api/v1/transfers/draft so tests can correlate per-file
 * paths — the minted path is /staging/{draftId:N}/f/{fileId:N}, unique per file. Arm BEFORE clicking Send;
 * waitForResponse resolves on the first matching response (one draft POST per test).
 */
export function captureDraft(page: Page): Promise<MintedDraft> {
  const response = page.waitForResponse((resp) => resp.request().method() === 'POST' && resp.url().includes('/api/v1/transfers/draft'))
  return response.then(async (r) => (await r.json()) as MintedDraft)
}

/** True for a block-stage PUT to the given minted blob path (the commit PUT has no comp=block). */
export function isStagePut(request: Request, blobPathSuffix: string): boolean {
  const url = request.url()
  if (!url.includes(blobPathSuffix) || !url.includes('comp=block')) return false
  if (request.method() !== 'PUT') return false
  // A stage PUT carries no content-length header; the commit PUT sends a small base64 block list.
  const cl = request.headers()['content-length']
  return cl == null || cl === String(BLOCK_SIZE)
}
