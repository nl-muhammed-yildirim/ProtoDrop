import { create } from 'zustand'

/**
 * UploadEngine store — the exact TA-8.3 contract (T-001-01).
 *
 * State: `{ files: [{id, name, size, status, progress}], overall: {sentBytes, totalBytes} }`.
 * API: `start(draft)`, `retry(fileId)`, `remove(fileId)`, `reset()` + staging (`addFiles`).
 *
 * T-001-01 is the staging half only: files are added/removed while `status` stays
 * `queued`; `start`/`retry` flip status (the block-upload loop arrives in T-001-06).
 */

export type FileStatus = 'queued' | 'uploading' | 'done' | 'failed'

export interface StagedFile {
  id: string
  name: string
  size: number
  /** queued|uploading|done|failed (TA-8.3) */
  status: FileStatus
  /** Bytes-based progress, 0..1 — `size * progress` is the bytes already sent. */
  progress: number
}

export interface DraftFile {
  fileId: string
  uploadUrl: string
}

export interface Draft {
  draftId: string
  expiresInSec: number
  files: DraftFile[]
}

interface UploadEngineState {
  files: StagedFile[]
  overall: { sentBytes: number; totalBytes: number }
}

interface UploadEngineActions {
  /** Stage files (selection order = SortOrder at finalize time). */
  addFiles(entries: Array<{ name: string; size: number; file?: File }>): string[]
  /** Stub — T-001-06 consumes the draft; param kept for the TA-8.3 API shape. */
  start(draft: Draft): void
  /** Stub — re-enqueues a failed file (T-001-08 owns backoff). */
  retry(fileId: string): void
  remove(fileId: string): void
  reset(): void
}

/** Staged ids → File handles. Registered by the drop zone (T-001-02); T-001-06 reads them in start(). */
export const stagedHandles = new Map<string, File>()
// Totals recomputed on every mutation (lockstep — T-001-04 pre-check reads them).
function totals(files: StagedFile[]): UploadEngineState['overall'] {
  return {
    totalBytes: files.reduce((sum, f) => sum + f.size, 0),
    sentBytes: files.reduce((sum, f) => sum + Math.round(f.size * f.progress), 0),
  }
}

export const useUploadEngine = create<UploadEngineState & UploadEngineActions>(
  (set) => ({
    files: [],
    overall: { sentBytes: 0, totalBytes: 0 },

    addFiles: (entries) => {
      const staged = entries.map((e) => {
        const file = { id: crypto.randomUUID(), name: e.name, size: e.size, status: 'queued' as FileStatus, progress: 0 }
        if (e.file) stagedHandles.set(file.id, e.file) // T-001-06 reads handles via stagedHandles.
        return file
      })
      set((state) => {
        const files = [...state.files, ...staged]
        return { files, overall: totals(files) }
      })
      return staged.map((f) => f.id)
    },

    start: () =>
      set((state) => {
        const files = state.files.map((f) =>
          f.status === 'queued' ? { ...f, status: 'uploading' as FileStatus } : f,
        )
        return { files, overall: totals(files) }
      }),

    retry: (fileId) =>
      set((state) => {
        const files = state.files.map((f) =>
          f.id === fileId && f.status === 'failed' ? { ...f, status: 'uploading' as FileStatus, progress: 0 } : f,
        )
        return { files, overall: totals(files) }
      }),

    remove: (fileId) => {
      stagedHandles.delete(fileId) // keep the handle registry in sync with the rows.
      set((state) => {
        const files = state.files.filter((f) => f.id !== fileId)
        return { files, overall: totals(files) }
      })
    },
    // Stub — T-001-06 consumes the draft's upload URLs; param kept for the TA-8.3 API shape.
    reset: () => { stagedHandles.clear(); set({ files: [], overall: { sentBytes: 0, totalBytes: 0 } }) },
  }),
)
