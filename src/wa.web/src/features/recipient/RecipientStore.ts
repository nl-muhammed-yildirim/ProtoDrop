import { create } from 'zustand';

// US-003-01: Recipient store — tracks the result of GET /api/v1/public/transfers/{linkId}.
// Spec: TA-4.2#4 (endpoint 4). No auth headers, no cookie.

export interface RecipientFile {
  fileId: string;
  name: string;
  sizeBytes: number;
}

interface RecipientState {
  status: 'loading' | 'active' | 'error';
  from: string;
  note: string | null;
  files: RecipientFile[];
  hasDownloadAll: boolean;
  downloadsLeft: number | null;
  passwordRequired: boolean;
  error: string | null;
  load(linkId: string): Promise<void>;
  reset(): void;
}

const initialState = {
  status: 'loading' as const,
  from: '',
  note: null as string | null,
  files: [] as RecipientFile[],
  hasDownloadAll: false,
  downloadsLeft: null as number | null,
  passwordRequired: false,
  error: null as string | null,
};

// US-003-01: generation counter to invalidate in-flight load() callbacks after reset.
let loadToken = 0;

export const useRecipientStore = create<RecipientState>()((set) => ({
  ...initialState,

  async load(linkId) {
    const token = ++loadToken;
    set({ status: 'loading', error: null });

    // Fix #6: validate linkId format before making the request.
    if (!/^[A-Z0-9]{8}$/.test(linkId)) {
      set({ status: 'error', error: 'Invalid link.' });
      return;
    }

    // AbortController with 30-second timeout (same pattern as TransferStore.finalize).
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 30_000);

    try {
      const response = await fetch(`/api/v1/public/transfers/${linkId}`, {
        signal: controller.signal,
      });

      if (response.ok) {
        const data = (await response.json()) as {
          from?: string;
          note?: string | null;
          files?: RecipientFile[];
          hasDownloadAll?: boolean;
          downloadsLeft?: number | null;
          passwordRequired?: boolean;
        };
        if (token !== loadToken) return; // stale — a newer load/reset occurred
        set({
          status: 'active',
          from: data.from ?? '',
          note: data.note ?? null,
          files: data.files ?? [],
          hasDownloadAll: data.hasDownloadAll ?? false,
          downloadsLeft: data.downloadsLeft ?? null,
          passwordRequired: data.passwordRequired ?? false,
          error: null,
        });
      } else {
        let message = `Load failed (${response.status}).`;
        try {
          const problem = (await response.json()) as { title?: string; detail?: string };
          if (problem.title) message = problem.title;
          else if (problem.detail) message = problem.detail;
        } catch {
          // Non-JSON error body — keep default message.
        }
        if (token !== loadToken) return; // stale — a newer load/reset occurred
        set({ status: 'error', error: message });
      }
    } catch {
      if (token !== loadToken) return; // stale — a newer load/reset occurred
      set({ status: 'error', error: 'Network error. Please try again.' });
    } finally {
      clearTimeout(timeout);
    }
  },

  reset() {
    loadToken++;
    set(initialState);
  },
}));
