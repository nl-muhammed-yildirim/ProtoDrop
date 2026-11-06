import { fileAttempts, stagedHandles } from '../../core/upload/UploadEngine'

/**
 * T-001-10 (TA-4.2#2) client half — the wire shape for POST /api/v1/transfers/draft/{draftId}/finalize.
 * Program.cs L210–218 parses exactly these fields: fileId + name + sizeBytes are required; contentType and
 * retries are optional (TriesGetProperty). The payload is built from the staged rows plus the minted fileIds
 * carried by `minted` — no network, no crypto, so it's unit-testable in isolation.
 */

/** One finalized file row — the Program.cs L210–218 wire shape (fileId/name/sizeBytes required; contentType/retries optional). */
export interface FinalizeFileRow {
  fileId: string
  name: string
  sizeBytes: number
  contentType: string | null
  retries: number
}

/** A file minted by the current send() — its staged row identity plus the minted fileId from the draft response. */
export interface MintedFile {
  stagedId: string
  name: string
  sizeBytes: number
  fileId: string
}

/** The finalize body — Program.cs L210–218 reads `files` as an array of FinalizeFileRow. */
export function buildFinalizePayload(minted: MintedFile[]): { files: FinalizeFileRow[] } {
  return {
    files: minted.map((m) => ({
      fileId: m.fileId, // minted by the draft POST — Program.cs L213 Guid.Parse on this string.
      name: m.name,
      sizeBytes: m.sizeBytes,
      contentType: stagedHandles.get(m.stagedId)?.type || null, // real MIME type when a File was registered; else null.
      retries: fileAttempts.get(m.stagedId)?.retries ?? 0, // FR-001-6 telemetry (T-001-08 → T-001-10).
    })),
  }
}

/**
 * EC-002-1 idempotency key — derived from the draftId so a retry of the same finalize reuses the same key.
 * The old architecture (commit 6a4f1e1 FinalizeButton) used exactly this form; mirror it for replay safety.
 */
export function finalizeKey(draftId: string): string {
  return `finalize-${draftId}`
}
