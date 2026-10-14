import { BlockBlobClient } from '@azure/storage-blob'

import { create } from 'zustand'

/**
 * UploadEngine store — the exact TA-8.3 contract (T-001-01).
 * State: `{ files: [{id, name, size, status, progress}], overall: {sentBytes, totalBytes} }`.
 * API: `start(draft)`, `retry(fileId)`, `remove(fileId)`, `reset()` + staging (`addFiles`).
 * T-001-06 adds the block-upload loop (TA-8.3): 8 MiB blocks staged per file with a worker pool of 4,
 * files serialized one at a time; progress is bytes-based and frozen on failure so `retry` resumes
 * from the last completed block (FR-001-5). T-001-08 adds per-block retry: each block gets up to 5 total
 * attempts with backoff RETRY_BACKOFF_MS before the row turns failed; per-file attempt counts are
 * tracked in `fileAttempts` for T-001-10's finalize payload (FR-001-6).
 */

export type FileStatus = 'queued' | 'uploading' | 'done' | 'failed'

/** Frozen web constants — docs/Open-Decisions-and-Constants.md Part 2 (TA-8.3) + FR-001-7. */
export const BLOCK_SIZE_BYTES = 8 * 1024 * 1024 // block size (browser upload)
export const MAX_PARALLEL_BLOCKS = 4 // parallelism per file, files serialized
/** FR-001-7: uploads above this never go as a single PUT — the block path is the only path. */
export const CHUNK_THRESHOLD_BYTES = 10 * 1024 * 1024
/** FR-001-6 (T-001-08): a block gets up to 5 total requests — the initial try plus one retry per delay below. */
export const MAX_ATTEMPTS = 5
/** Retry delays in ms, applied between consecutive attempts: 0.5 s → 4 s exponential (T-001-08). */
export const RETRY_BACKOFF_MS = [500, 1000, 2000, 4000] as readonly number[]

export interface StagedFile {
  id: string
  name: string
  size: number
  /** queued|uploading|done/failed (TA-8.3) */
  status: FileStatus
  /** Bytes-based progress, 0..1 — `size * progress` is the bytes already sent. */
  progress: number
}

export interface DraftFile { fileId: string; uploadUrl: string }
export interface Draft { draftId: string; expiresInSec: number; files: DraftFile[] }

interface UploadEngineState { files: StagedFile[]; overall: { sentBytes: number; totalBytes: number } }

interface UploadEngineActions {
  addFiles(entries: Array<{ name: string; size: number; file?: File }>): string[]
  start(draft: Draft): Promise<void>
  retry(fileId: string): Promise<void>
  remove(fileId: string): void
  reset(): void
}

export const stagedHandles = new Map<string, File>() // drop zone registers real Files by staged id; start() reads them
// Minted upload URLs survive staging removals so retry can re-enter the same block path (FR-001-5).
const uploadUrls = new Map<string, string>()
// Within-session resume (FR-001-5): successful blocks survive failure — the engine skips offsets already sent.
const completedOffsets = new Map<string, Set<number>>()
/** FR-001-6 telemetry plumbing (T-001-08 → T-001-10 finalize payload): per-file retry counts + last error code; stage failures only — commit retries are re-entry via `retry(fileId)`. */
export const fileAttempts = new Map<string, { retries: number; lastErrorCode: string | null }>()
/** Replaces a file's FR-001-6 record (a retry resets it so upload_failed counts stay per failure run). */
function recordAttempts(id: string, retries: number, lastErrorCode: string | null): void {
  fileAttempts.set(id, { retries, lastErrorCode })
}
/** Overridable sleep so tests can stub the backoff near-zero (T-001-08). */
let sleepFn: (ms: number) => Promise<void> = (ms) => new Promise((resolve) => { setTimeout(resolve, ms) })
export const setSleepFnForTests = (fn: (ms: number) => Promise<void>): void => { sleepFn = fn }

// Totals recomputed on every mutation (lockstep — T-001-04 pre-check reads them).
function totals(files: StagedFile[]): UploadEngineState['overall'] {
  return { totalBytes: files.reduce((sum, f) => sum + f.size, 0), sentBytes: files.reduce((sum, f) => sum + Math.round(f.size * f.progress), 0) }
}

function setProgress(id: string, progress: number): void {
  const state = useUploadEngine.getState()
  const files = state.files.map((f) => (f.id === id ? { ...f, progress } : f))
  useUploadEngine.setState({ files, overall: totals(files) })
}

function setStatus(id: string, status: FileStatus): void {
  const state = useUploadEngine.getState()
  const files = state.files.map((f) => (f.id === id ? { ...f, status } : f))
  useUploadEngine.setState({ files, overall: totals(files) })
}

function blockOffsets(size: number): number[] {
  const offsets: number[] = []
  for (let off = 0; off < size; off += BLOCK_SIZE_BYTES) offsets.push(off)
  return offsets
}

/** Deterministic per (file, offset); base64 of ASCII ≤64 bytes. */
function blockId(fileSize: number, offset: number): string {
  return btoa(`${fileSize}:${offset}`)
}

/** Progress = completed bytes / size; the done set only grows within a run, so it never decreases. */
function progressFor(id: string, size: number): number {
  if (size === 0) return 1
  const done = completedOffsets.get(id) ?? new Set<number>()
  let sentBytes = 0
  for (const off of blockOffsets(size)) if (done.has(off)) sentBytes += Math.min(BLOCK_SIZE_BYTES, size - off)
  return sentBytes / size
}

async function uploadFile(
  id: string,
  file: File | undefined,
  size: number,
  url: string | undefined,
): Promise<boolean> {
  if (size === 0) { setStatus(id, 'done'); setProgress(id, 1); return true } // Azure rejects an empty blocklist — no client call needed

  const done = completedOffsets.get(id) ?? new Set<number>()
  completedOffsets.set(id, done)
  const offsets = blockOffsets(size).filter((off) => !done.has(off))
  if (file == null || url == null) { setStatus(id, 'failed'); return false } // nothing to send with

  const client = new BlockBlobClient(url, undefined, { retryOptions: { maxTries: 1 } })
  let nextIndex = 0 // shared cursor — atomic between awaits in single-threaded JS (TA-8.3 pool)
  const worker = async (): Promise<boolean> => {
    for (;;) {
      const i = nextIndex++
      if (i >= offsets.length) break
      const off = offsets[i]
      const len = Math.min(BLOCK_SIZE_BYTES, size - off)
      // FR-001-6: up to MAX_ATTEMPTS total requests per block; deterministic block IDs → re-issue is idempotent.
      let staged = false
      for (let attempt = 0; attempt < MAX_ATTEMPTS && !staged; attempt += 1) {
        try {
          await client.stageBlock(blockId(size, off), file.slice(off, off + len), len)
          done.add(off)
          setProgress(id, progressFor(id, size)) // bytes uploaded so far (TA-8.3 — bytes-based)
          staged = true
        } catch (error: unknown) {
          const entry = fileAttempts.get(id) ?? { retries: 0, lastErrorCode: null } // record is re-armed by retry() before each run
          if (attempt < MAX_ATTEMPTS - 1) {
            entry.retries += 1 // a retry is coming — T-001-10's upload_failed count reads this
            await sleepFn(RETRY_BACKOFF_MS[attempt]) // backoff between attempts; the final attempt waits nothing
          }
          recordAttempts(id, entry.retries, error instanceof Error ? error.name : 'unknown')
        }
      }
      if (!staged) return false // all 5 attempts exhausted — remaining blocks stay resumable (FR-001-5/6)
    }
    return true
  }

  try {
    const ok = await Promise.all(Array.from({ length: MAX_PARALLEL_BLOCKS }, () => worker()))
    if (!ok.every(Boolean)) throw new Error('block stage failed') // wait for ALL workers before returning — no stragglers into retry (FR-001-5)
  } catch {
    setStatus(id, 'failed') // progress frozen at last completed bytes; row-local Retry (T-001-08)
    return false
  }

  try {
    await client.commitBlockList(blockOffsets(size).map((off) => blockId(size, off)))
  } catch {
    setStatus(id, 'failed') // staged blocks stay resumable; progress frozen (FR-001-5)
    return false
  }

  setProgress(id, 1)
  setStatus(id, 'done')
  return true
}

export const useUploadEngine = create<UploadEngineState & UploadEngineActions>((set) => ({
  files: [], overall: { sentBytes: 0, totalBytes: 0 },
  addFiles: (entries) => {
    const staged = entries.map((e) => {
      const file = { id: crypto.randomUUID(), name: e.name, size: e.size, status: 'queued' as FileStatus, progress: 0 }
      if (e.file) stagedHandles.set(file.id, e.file)
      return file
    })
    set((state) => { const files = [...state.files, ...staged]; return { files, overall: totals(files) } })
    return staged.map((f) => f.id)
  },
  start: (draft) => {
    // Minted URLs registered up front so retry can re-enter even after staging rows are removed.
    const allFiles = useUploadEngine.getState().files
    const queued = allFiles.filter((f) => f.status === 'queued')
    for (const stagedFile of queued) uploadUrls.set(stagedFile.id, draft.files[allFiles.findIndex((f) => f.id === stagedFile.id)]?.uploadUrl ?? '')
    return (async () => {
      for (const stagedFile of queued) { // files strictly one at a time — TA-8.3 serialization
        setStatus(stagedFile.id, 'uploading')
        await uploadFile(stagedFile.id, stagedHandles.get(stagedFile.id), stagedFile.size, uploadUrls.get(stagedFile.id))
      }
    })() // never rejects: per-file errors surface as failed status (TA-8.3)
  },
  /** Retry (FR-001-6): re-enter uploadFile with a zeroed attempt record — finished blocks are skipped via completedOffsets, so no finished bytes re-uploaded. */
  retry: (fileId) => {
    const file = useUploadEngine.getState().files.find((f) => f.id === fileId)
    return (async () => {
      if (file?.status !== 'failed') return
      recordAttempts(fileId, 0, null) // the retry re-arms the FR-001-6 counter — a subsequent failure is fresh telemetry data
      setStatus(fileId, 'uploading') // progress NOT reset — resume skips finished bytes (FR-001-5)
      await uploadFile(fileId, stagedHandles.get(fileId), file.size, uploadUrls.get(fileId))
    })()
  },
  remove: (fileId) => { completedOffsets.delete(fileId); fileAttempts.delete(fileId); set((state) => { const files = state.files.filter((f) => f.id !== fileId); return { files, overall: totals(files) } }) },
  reset: () => { stagedHandles.clear(); uploadUrls.clear(); completedOffsets.clear(); fileAttempts.clear(); set({ files: [], overall: { sentBytes: 0, totalBytes: 0 } }) },
}))
