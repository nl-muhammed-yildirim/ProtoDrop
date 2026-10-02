import { create } from 'zustand';

// US-002-01: Transfer store — tracks finalize status and the resulting linkId.
// Spec: POST /api/v1/transfers/finalize (TA-4.2#3).

interface TransferState {
  status: 'idle' | 'finalizing' | 'ready';
  linkId: string | null;
  finalizeError: string | null;
  draftId: string | null;
  // US-002-02: send state (EC-006-1).
  sendStatus: 'idle' | 'sending' | 'sent';
  recipients: string[];
  sendError: string | null;
  finalize(draftId: string, idempotencyKey: string): Promise<void>;
  send(linkId: string, recipients: string[], idempotencyKey: string, password?: string): Promise<void>;
  setDraftId(id: string): void;
  reset(): void;
  resetSend(): void;
}

const initialState = {
  status: 'idle' as const,
  linkId: null,
  finalizeError: null,
  draftId: null,
  sendStatus: 'idle' as const,
  recipients: [] as string[],
  sendError: null,
};

// US-002-02: generation counter to invalidate in-flight send() callbacks after reset/resetSend.
let sendToken = 0;

export const useTransferStore = create<TransferState>()((set) => ({
  ...initialState,

  // US-002-01: POST /api/v1/transfers/finalize with Idempotency-Key header (EC-002-1).
  async finalize(draftId, idempotencyKey) {
    set({ status: 'finalizing', finalizeError: null });

    // Fix 11: AbortController with 30-second timeout to prevent permanent "finalizing" state.
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 30_000);

    try {
      const response = await fetch('/api/v1/transfers/finalize', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
        },
        body: JSON.stringify({ draftId }),
        signal: controller.signal,
      });

      if (response.ok) {
        const data = (await response.json()) as { linkId?: string };
        // Fix 11: validate linkId to prevent undefined propagation.
        if (!data.linkId || typeof data.linkId !== 'string') {
          set({ status: 'idle', finalizeError: 'Invalid response from server.' });
          return;
        }
        set({ status: 'ready', linkId: data.linkId, finalizeError: null });
      } else {
        let message = `Finalize failed (${response.status}).`;
        try {
          const problem = (await response.json()) as { title?: string; detail?: string };
          if (problem.title) message = problem.title;
          else if (problem.detail) message = problem.detail;
        } catch {
          // Non-JSON error body — keep default message.
        }
        set({ status: 'idle', finalizeError: message });
      }
    } catch {
      set({ status: 'idle', finalizeError: 'Network error. Please try again.' });
    } finally {
      clearTimeout(timeout);
    }
  },

  // US-002-02: POST /api/v1/transfers/send with Idempotency-Key header (EC-006-1).
  async send(linkId, recipients, idempotencyKey, password) {
    const token = ++sendToken;
    set({ sendStatus: 'sending', sendError: null });

    // US-002-02: AbortController with 30-second timeout to prevent permanent "sending" state.
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 30_000);

    try {
      const response = await fetch('/api/v1/transfers/send', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
        },
        body: JSON.stringify({ linkId, recipients, ...(password ? { password } : {}) }),
        signal: controller.signal,
      });

      if (response.ok) {
        if (token !== sendToken) return; // stale — a newer send/reset occurred
        set({ sendStatus: 'sent', recipients, sendError: null });
      } else {
        let message = `Send failed (${response.status}).`;
        try {
          const problem = (await response.json()) as { title?: string; detail?: string };
          if (problem.title) message = problem.title;
          else if (problem.detail) message = problem.detail;
        } catch {
          // Non-JSON error body — keep default message.
        }
        if (token !== sendToken) return; // stale — a newer send/reset occurred
        set({ sendStatus: 'idle', sendError: message });
      }
    } catch {
      if (token !== sendToken) return; // stale — a newer send/reset occurred
      set({ sendStatus: 'idle', sendError: 'Network error. Please try again.' });
    } finally {
      clearTimeout(timeout);
    }
  },

  setDraftId(id) {
    set({ draftId: id });
  },

  reset() {
    // US-002-02: invalidate any in-flight send() callback.
    sendToken++;
    set(initialState);
  },

  // US-002-02: reset only send-related fields (leave status/linkId/draftId intact).
  resetSend() {
    // US-002-02: invalidate any in-flight send() callback.
    sendToken++;
    set({ sendStatus: 'idle', recipients: [], sendError: null });
  },
}));

// US-002-01: build the public transfer URL from a linkId (AC-002-1).
export function getPublicLink(linkId: string): string {
  return `${window.location.origin}/t/${linkId}`;
}
