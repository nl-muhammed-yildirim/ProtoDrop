import { create } from 'zustand';
import { showToast } from '../ui/Toast';

// TA-8.3 UploadEngine contract (docs/03-technical-architecture.md §TA-8.3):
//   State: { files: [{id, name, size, status: queued|uploading|done|failed, progress}],
//            overall: {sentBytes, totalBytes} }
//   API: start(draft), retry(fileId), remove(fileId), reset()
// US-001-05: block-level retry with exponential backoff (FR-001-6 / AC-001-3).

export type UploadFileStatus = 'queued' | 'uploading' | 'done' | 'failed';

export interface UploadFile {
  id: string;
  name: string;
  size: number;
  status: UploadFileStatus;
  /** Fraction uploaded, 0..1. Byte-based: (completedBlocks * BLOCK_SIZE) / fileSize. */
  progress: number;
}

/** Draft from POST /api/v1/transfers/draft (TA-4.2#1). */
export type UploadDraft = Record<string, unknown>;

interface UploadEngineApi {
  files: UploadFile[];
  overall: { sentBytes: number; totalBytes: number };
  /** Stage files in selection order; duplicates allowed (EC-001-2). US-001-01. */
  addFiles(files: File[]): void;
  remove(fileId: string): void; // TA-8.3
  reset(): void; // TA-8.3
  start(draft: UploadDraft): void; // TA-8.3 — US-001-05: block upload with retry
  retry(fileId: string): void; // TA-8.3 — US-001-05: resume from saved block index
}

// EC-001-1: the later block upload continues on the original File objects, so the
// handles live here instead of in the serialized TA-8.3 state above.
const fileHandles = new Map<string, File>();

// US-001-05: store per-file upload URL from the draft so retry() can resume (Fix 1).
const uploadUrls = new Map<string, string>();

// US-001-05: track completed block indices as a Set per file for precise resume (FR-001-6, Fix 2).
const completedBlocks = new Map<string, Set<number>>();

// US-001-05: double-click guard — track in-flight retry file IDs (Fix 3).
const activeRetries = new Set<string>();

// US-001-05: fire toast only once per session on first failure (AC-001-3).
let failureToastShown = false;

// US-001-05: 8 MiB blocks (TA-8.3).
const BLOCK_SIZE = 8 * 1024 * 1024;

// US-001-05: exponential backoff delays for up to 5 retry attempts (FR-001-6).
const RETRY_DELAYS_MS = [500, 1000, 2000, 4000, 8000];

// US-001-05: parallelism per file (TA-8.3).
const PARALLELISM = 4;

/** US-001-05: default block transport — PUT the block to the SAS URL. */
type UploadFn = (block: Blob, uploadUrl: string) => Promise<void>;

let uploadFn: UploadFn = async (block, uploadUrl) => {
  const response = await fetch(uploadUrl, { method: 'PUT', body: block });
  if (!response.ok) throw new Error(`Block upload failed: ${response.status}`);
};

/** US-001-05: default delay function. */
let delayFn: (ms: number) => Promise<void> = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// US-001-05: test seams for mocking transport and delays in unit tests.
export const __setUploadFn = (fn: UploadFn): void => {
  uploadFn = fn;
};

export const __setDelayFn = (fn: (ms: number) => Promise<void>): void => {
  delayFn = fn;
};

/** US-001-05: compute byte-based progress fraction. */
const computeProgress = (completedBlocks: number, fileSize: number): number => {
  if (fileSize === 0) return 1; // EC-001-3: zero-byte guard
  return Math.min((completedBlocks * BLOCK_SIZE) / fileSize, 1);
};

const summarize = (files: UploadFile[]) => ({
  sentBytes: files.reduce((sum, file) => sum + Math.round(file.progress * file.size), 0),
  totalBytes: files.reduce((sum, file) => sum + file.size, 0),
});

/** US-001-05: upload a single block with retry sub-loop (up to 5 attempts). */
async function uploadBlockWithRetry(
  _fileId: string,
  blockIndex: number,
  file: File,
  uploadUrl: string
): Promise<void> {
  const start = blockIndex * BLOCK_SIZE;
  const end = Math.min(start + BLOCK_SIZE, file.size);
  const block = file.slice(start, end);

  for (let attempt = 0; attempt <= RETRY_DELAYS_MS.length; attempt++) {
    try {
      await uploadFn(block, uploadUrl);
      return; // success — exit retry loop
    } catch {
      if (attempt === RETRY_DELAYS_MS.length) throw new Error(`Block ${blockIndex} failed after ${RETRY_DELAYS_MS.length + 1} attempts`);
      await delayFn(RETRY_DELAYS_MS[attempt]);
    }
  }
}

/** US-001-05: upload all blocks of a file with parallelism 4. */
async function uploadFile(
  fileId: string,
  file: File,
  uploadUrl: string,
  set: (fn: (state: UploadEngineApi) => Partial<UploadEngineApi>) => void
): Promise<void> {
  const totalBlocks = Math.ceil(file.size / BLOCK_SIZE);
  let completedSet = completedBlocks.get(fileId) ?? new Set<number>();

  // US-001-05: resume from saved block indices — skip already-completed blocks (FR-001-6).
  if (completedSet.size >= totalBlocks) {
    set((state) => ({
      files: state.files.map((f) =>
        f.id === fileId ? { ...f, status: 'done' as const, progress: 1 } : f
      ),
      overall: summarize(
        state.files.map((f) =>
          f.id === fileId ? { ...f, status: 'done' as const, progress: 1 } : f
        )
      ),
    }));
    return;
  }

  // US-001-05: process blocks in batches of PARALLELISM (TA-8.3).
  while (completedSet.size < totalBlocks) {
    // Fix 2: find block indices not yet completed, take the next PARALLELISM.
    const remaining = Array.from(
      { length: totalBlocks },
      (_, i) => i
    ).filter((i) => !completedSet.has(i));

    const batch = remaining.slice(0, PARALLELISM);

    // US-001-05: upload batch in parallel; track which blocks succeeded.
    const results = await Promise.allSettled(
      batch.map(async (blockIndex) => {
        await uploadBlockWithRetry(fileId, blockIndex, file, uploadUrl);
        return blockIndex;
      })
    );

    // Fix 2: add only the blocks that actually succeeded to the set.
    const failed = results.some((r) => r.status === 'rejected');

    if (failed) {
      // US-001-05: mark file as failed, preserve progress (AC-001-3).
      // Fix 2: add only successfully completed block indices to the set.
      results.forEach((r, idx) => {
        if (r.status === 'fulfilled') {
          completedSet.add(batch[idx]);
        }
      });
      completedBlocks.set(fileId, completedSet);

      set((state) => {
        const files = state.files.map((f) =>
          f.id === fileId
            ? { ...f, status: 'failed' as const, progress: computeProgress(completedSet.size, file.size) }
            : f
        );
        return { files, overall: summarize(files) };
      });
      return;
    }

    // All blocks in batch succeeded — add all to the set.
    for (const blockIndex of batch) {
      completedSet.add(blockIndex);
    }
    completedBlocks.set(fileId, completedSet);

    set((state) => {
      const files = state.files.map((f) =>
        f.id === fileId
          ? { ...f, progress: computeProgress(completedSet.size, file.size) }
          : f
      );
      return { files, overall: summarize(files) };
    });
  }

  // US-001-05: all blocks done — mark file as complete.
  set((state) => {
    const files = state.files.map((f) =>
      f.id === fileId ? { ...f, status: 'done' as const, progress: 1 } : f
    );
    return { files, overall: summarize(files) };
  });

  // Fix 4: clean up Maps after file completes (memory leak prevention).
  fileHandles.delete(fileId);
  completedBlocks.delete(fileId);
}

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
    uploadUrls.delete(fileId); // Fix 1: clear stored URL on removal
    completedBlocks.delete(fileId); // US-001-05: clear progress on removal
    activeRetries.delete(fileId); // Fix 3: clear retry guard on removal
    set((state) => {
      const files = state.files.filter((file) => file.id !== fileId);
      return { files, overall: summarize(files) };
    });
  },

  reset() {
    fileHandles.clear();
    uploadUrls.clear(); // Fix 1: clear all stored URLs
    completedBlocks.clear(); // US-001-05: clear all block progress
    activeRetries.clear(); // Fix 3: clear all retry guards
    failureToastShown = false; // US-001-05: reset toast flag (AC-001-3)
    set({ files: [], overall: { sentBytes: 0, totalBytes: 0 } });
  },

  start(draft: UploadDraft) {
    // US-001-05: serialized per-file upload loop (FR-001-4).
    const files = useUploadEngine.getState().files;
    const draftFiles = (draft as { files?: Array<{ uploadUrl: string }> }).files ?? [];

    // Fix 1: store each file's upload URL so retry() can resume with the correct URL.
    for (let i = 0; i < files.length; i++) {
      uploadUrls.set(files[i].id, draftFiles[i]?.uploadUrl ?? '');
    }

    // Mark all files as uploading.
    set((state) => ({
      files: state.files.map((file) => ({ ...file, status: 'uploading' as const })),
    }));

    // US-001-05: upload files one at a time — file i+1 starts only after file i completes or fails.
    void (async () => {
      for (let i = 0; i < files.length; i++) {
        const file = files[i];
        const fileHandle = fileHandles.get(file.id);
        if (!fileHandle) continue;

        // US-001-05: zero-byte guard (EC-001-3).
        if (file.size === 0) {
          set((state) => ({
            files: state.files.map((f) =>
              f.id === file.id ? { ...f, status: 'done' as const, progress: 1 } : f
            ),
          }));
          continue;
        }

        const uploadUrl = draftFiles[i]?.uploadUrl ?? '';
        try {
          await uploadFile(file.id, fileHandle, uploadUrl, set);
        } catch {
          // US-001-05: mark file as failed (AC-001-3).
          set((state) => ({
            files: state.files.map((f) =>
              f.id === file.id ? { ...f, status: 'failed' as const } : f
            ),
          }));
        }

        // US-001-05: fire toast on first failure in session (AC-001-3).
        if (!failureToastShown) {
          const currentFiles = useUploadEngine.getState().files;
          if (currentFiles.some((f) => f.status === 'failed')) {
            failureToastShown = true;
            showToast('info', 'Some files need attention');
          }
        }
      }
    })();
  },

  retry(fileId: string) {
    // US-001-05: reset status to uploading and resume from saved block index (FR-001-6).
    const file = useUploadEngine.getState().files.find((f) => f.id === fileId);
    if (!file || file.status !== 'failed') return;

    // Fix 3: double-click guard — ignore retry if already in flight.
    if (activeRetries.has(fileId)) return;

    set((state) => ({
      files: state.files.map((f) =>
        f.id === fileId ? { ...f, status: 'uploading' as const } : f
      ),
    }));

    const fileHandle = fileHandles.get(fileId);
    if (!fileHandle) return;

    // Fix 1: read the stored upload URL instead of passing empty string.
    const uploadUrl = uploadUrls.get(fileId) ?? '';

    activeRetries.add(fileId); // Fix 3: mark retry as in-flight.

    // US-001-05: resume from the saved block index — no re-upload of finished blocks.
    void (async () => {
      try {
        await uploadFile(fileId, fileHandle, uploadUrl, set);
      } catch {
        set((state) => ({
          files: state.files.map((f) =>
            f.id === fileId ? { ...f, status: 'failed' as const } : f
          ),
        }));
      } finally {
        activeRetries.delete(fileId); // Fix 3: release retry guard.
      }
    })();
  },
}));
