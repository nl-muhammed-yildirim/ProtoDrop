import { describe, expect, it } from 'vitest'

import { buildFinalizePayload, finalizeKey, type MintedFile } from './finalize'
import { fileAttempts, stagedHandles } from '../../core/upload/UploadEngine'

/**
 * T-001-10 (TA-4.2#2) client half — the pure finalize-payload builder. No fetch/crypto here: it reads only the
 * module-level stagedHandles + fileAttempts maps that UploadEngine exposes, so these tests are hermetic.
 */
describe('buildFinalizePayload', () => {
  const minted: MintedFile[] = [
    { stagedId: 's1', name: 'a.bin', sizeBytes: 48, fileId: 'f-a' },
    { stagedId: 's2', name: 'b.txt', sizeBytes: 7, fileId: 'f-b' },
  ]

  it('emits Program.cs L210–218 wire shape — fileId/name/sizeBytes required, contentType+retries present (AC-002-1)', () => {
    const payload = buildFinalizePayload(minted)
    expect(payload.files).toHaveLength(2)
    // Every field Program.cs L213 Guid.Parse / GetProperty reads is present and correctly typed.
    for (const row of payload.files) {
      expect(typeof row.fileId).toBe('string')
      expect(typeof row.name).toBe('string')
      expect(typeof row.sizeBytes).toBe('number')
      expect(row.contentType === null || typeof row.contentType === 'string').toBe(true)
      expect(Number.isInteger(row.retries)).toBe(true)
    }
  })

  it('fileId comes from the minted draft (position i), not the staged id — Program.cs L213 parses it as Guid', () => {
    const payload = buildFinalizePayload(minted)
    expect(payload.files[0]!.fileId).toBe('f-a') // minted, NOT 's1'
    expect(payload.files[1]!.fileId).toBe('f-b')
  })

  it('contentType is the real MIME type when a File was registered; null otherwise (L216 TryGetProperty)', () => {
    stagedHandles.set('s1', new File(['x'.repeat(48)], 'a.bin', { type: 'application/octet-stream' }))
    // s2 has no registered handle — the guest staging path.
    const payload = buildFinalizePayload(minted)
    expect(payload.files[0]!.contentType).toBe('application/octet-stream')
    expect(payload.files[1]!.contentType).toBeNull()
  })

  it('retries reflects fileAttempts (FR-001-6 telemetry); absent entries default to 0', () => {
    fileAttempts.set('s1', { retries: 2, lastErrorCode: 'AbortError' })
    // s2 never recorded an attempt — the fresh-path default.
    const payload = buildFinalizePayload(minted)
    expect(payload.files[0]!.retries).toBe(2)
    expect(payload.files[1]!.retries).toBe(0)
  })

  it('empty minted list → empty files array (the allDone guard prevents this, but the builder is total)', () => {
    expect(buildFinalizePayload([]).files).toEqual([])
  })
})

describe('finalizeKey', () => {
  it('is derived from draftId so a retry of the same finalize reuses the key (EC-002-1)', () => {
    const d = 'abcdef123456'
    expect(finalizeKey(d)).toBe(`finalize-${d}`)
    // Same draft → same key; different draft → different key.
    expect(finalizeKey(d)).toBe(finalizeKey(d))
    expect(finalizeKey('other')).not.toBe(finalizeKey(d))
  })
})
