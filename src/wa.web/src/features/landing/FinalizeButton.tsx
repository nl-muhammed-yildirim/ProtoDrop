import { useRef } from 'react';
import { useTransferStore } from '../../core/upload/TransferStore';
import { useUploadEngine } from '../../core/upload/UploadEngine';

// US-002-01: "Get my link" button — appears when all files are done and transfer is not yet ready.
// Triggers POST /api/v1/transfers/finalize with an Idempotency-Key (EC-002-1).

export function FinalizeButton() {
  const files = useUploadEngine((state) => state.files);
  const draftId = useTransferStore((state) => state.draftId);
  const status = useTransferStore((state) => state.status);
  const finalize = useTransferStore((state) => state.finalize);

  // US-002-01: double-click guard.
  const isFinalizingRef = useRef(false);

  // Render only when all files are done and transfer is not yet ready.
  const allDone = files.length > 0 && files.every((f) => f.status === 'done');
  if (!allDone || status === 'ready' || !draftId) return null;

  const isFinalizing = status === 'finalizing';

  const handleFinalize = async (): Promise<void> => {
    if (isFinalizingRef.current) return;
    isFinalizingRef.current = true;

    try {
      // Fix 10: derive idempotency key from draftId so retries use the same key (EC-002-1).
      const idempotencyKey = `finalize-${draftId}`;
      await finalize(draftId, idempotencyKey);
    } finally {
      isFinalizingRef.current = false;
    }
  };

  return (
    <button
      type="button"
      className="btn btn-primary finalize-button"
      onClick={handleFinalize}
      disabled={isFinalizing}
    >
      {isFinalizing ? 'Preparing…' : 'Get my link'}
    </button>
  );
}
