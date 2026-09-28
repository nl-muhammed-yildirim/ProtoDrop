import { create } from 'zustand';

// TA-8.3 UploadEngine contract (docs/03-technical-architecture.md §TA-8.3):
//   State: { files: [{id, name, size, status: queued|uploading|done|failed, progress}],
//            overall: {sentBytes, totalBytes} }
//   API: start(draft), retry(fileId), remove(fileId), reset()
// US-001-01 stages client-side only — nothing is uploaded until "Send." (US-001-03).

export type UploadFileStatus = 'queued' | 'uploading' | 'done' | 'failed';

export interface UploadFile {
  id: string;
  name: string;
  size: number;
  status: UploadFileStatus;
  /** Fraction uploaded, 0..1. Stays 0 until start(). */
  progress: number;
}

/** Draft from POST /api/v1/transfers/draft (TA-4.2#1) — full DTO lands with US-001-03. */
export type UploadDraft = Record<string, unknown>;

interface UploadEngineApi {
  files: UploadFile[];
  overall: { sentBytes: number; totalBytes: number };
  /** Stage files in selection order; duplicates allowed (EC-001-2). US-001-01. */
  addFiles(files: File[]): void;
  remove(fileId: string): void; // TA-8.3
  reset(): void; // TA-8.3
  start(draft: UploadDraft): void; // TA-8.3 — block upload lands with US-001-03/T-004
  retry(fileId: string): void; // TA-8.3 — block upload lands with US-001-03/T-004
}

// EC-001-1: the later block upload continues on the original File objects, so the
// handles live here instead of in the serialized TA-8.3 state above.
const fileHandles = new Map<string, File>();

const summarize = (files: UploadFile[]) => ({
  // No bytes written before "Send." (US-001-01 AC) — sentBytes moves only in start().
  sentBytes: 0,
  totalBytes: files.reduce((sum, file) => sum + file.size, 0),
});

export const useUploadEngine = create<UploadEngineApi>()((set) => ({
  files: [],
  overall: { sentBytes: 0, totalBytes: 0 },

  addFiles(incoming: File[]) {
    const staged: UploadFile[] = incoming.map((file) => {
      const id = crypto.randomUUID();
      fileHandles.set(id, file);
      // Folder flattening via webkitRelativePath (US-001-01): "Photos/a.png" stages as "a.png".
      const path = file.webkitRelativePath ?? '';
      const name = path ? path.split('/').pop() || file.name : file.name;
      return { id, name, size: file.size, status: 'queued' as const, progress: 0 };
    });
    set((state) => {
      const files = [...state.files, ...staged];
      return { files, overall: summarize(files) };
    });
  },

  remove(fileId: string) {
    fileHandles.delete(fileId);
    set((state) => {
      const files = state.files.filter((file) => file.id !== fileId);
      return { files, overall: summarize(files) };
    });
  },

  reset() {
    fileHandles.clear();
    set({ files: [], overall: { sentBytes: 0, totalBytes: 0 } });
  },

  start(_draft: UploadDraft) {
    // TODO(US-001-03/T-004): block upload — 8 MiB blocks, parallelism 4 per file (TA-8.3).
  },

  retry(_fileId: string) {
    // TODO(US-001-03/T-004): block upload — stop after 5 block retries (F-TRF-001-6, TA-8.3).
  },
}));
