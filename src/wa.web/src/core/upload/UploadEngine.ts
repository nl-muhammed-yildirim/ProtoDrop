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
  addFiles(entries: Array<{ name: string; size: number }>): void
  /** Stub — T-001-06 consumes the draft; param kept for the TA-8.3 API shape. */
  start(draft: Draft): void
  /** Stub — re-enqueues a failed file (T-001-08 owns backoff). */
  retry(fileId: string): void
  remove(fileId: string): void
  reset(): void
}

// Totals recomputed on every mutation (3+ call sites, lockstep — T-001-04 pre-check reads them).
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

    addFiles: (entries) =>
      set((state) => {
        // Selection order preserved (T-001-03); duplicate names both kept (EC-001-2).
        const staged = entries.map((e) => ({
          id: crypto.randomUUID(),
          name: e.name,
          size: e.size,
          status: 'queued' as FileStatus,
          progress: 0,
        }))
        return { files: [...state.files, ...staged], overall: totals([...state.files, ...staged]) }
      }),

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

    remove: (fileId) =>
      set((state) => {
        const files = state.files.filter((f) => f.id !== fileId)
        return { files, overall: totals(files) }
      }),

    // Stub — T-001-06 consumes the draft's upload URLs; param kept for the TA-8.3 API shape.
    reset: () => set({ files: [], overall: { sentBytes: 0, totalBytes: 0 } }),
  }),
)
