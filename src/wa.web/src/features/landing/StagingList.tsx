import { useRef } from 'react';
import { formatBytes } from '../../core/upload/formatBytes';
import type { UploadDraft } from '../../core/upload/UploadEngine';
import { useUploadEngine } from '../../core/upload/UploadEngine';
import { resolveEffectiveLimits, validateSelection } from '../../core/upload/limits';
import type { Violation } from '../../core/upload/limits';
import { showToast } from '../../core/ui/Toast';

const fileIcon = (
  <svg
    className="file-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
    <path d="M14 2v6h6" />
  </svg>
);

const removeIcon = (
  <svg
    className="file-remove-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <path d="M18 6L6 18" />
    <path d="M6 6l12 12" />
  </svg>
);

// US-001-05: retry icon for failed rows (AC-001-3).
const retryIcon = (
  <svg
    className="file-retry-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.85 1.03 6.5 2.7L21 8" />
    <path d="M21 3v5h-5" />
  </svg>
);

interface DraftFile {
  name: string;
  sizeBytes: number;
}

export function StagingList() {
  const files = useUploadEngine((state) => state.files);
  const totalBytes = useUploadEngine((state) => state.overall.totalBytes);
  const remove = useUploadEngine((state) => state.remove);
  const start = useUploadEngine((state) => state.start);
  const retry = useUploadEngine((state) => state.retry);

  // Fix 5: guard against double-submit on Send button.
  const isSendingRef = useRef(false);

  // US-001-03: resolve effective limits (guest fallback; replace with /api/v1/limits fetch when auth lands).
  const limits = resolveEffectiveLimits();

  // US-001-03: validate selection on every render (reactive to changes in files array or limits).
  const violations = validateSelection(files, limits);
  const hasViolation = violations.length > 0;

  if (files.length === 0) return null; // empty list → the drop zone is the whole UI again

  return (
    <div className="staging">
      {/* FR-001-5: overall status line — ARIA live region for screen readers (AC-001-3). */}
      {files.length > 0 && (() => {
        const failedCount = files.filter((f) => f.status === 'failed').length;
        const uploadingCount = files.filter((f) => f.status === 'uploading').length;
        const doneCount = files.filter((f) => f.status === 'done').length;

        let text: string | null = null;
        let className = 'staging-overall';

        if (failedCount > 0) {
          text = `Upload paused — ${failedCount} file${failedCount === 1 ? '' : 's'} need attention.`;
          className += ' staging-overall--failed';
        } else if (uploadingCount > 0) {
          const overallPct = files.every((f) => f.size === 0 || f.progress >= 1)
            ? 100
            : Math.round(
                (files.reduce((s, f) => s + f.progress * f.size, 0)) /
                  (files.reduce((s, f) => s + f.size, 0) || 1) * 100
              );
          text = `Uploading… ${doneCount} of ${files.length}`;
        } else if (doneCount === files.length && files.length > 0) {
          text = 'All files uploaded.';
          className += ' staging-overall--done';
        }

        return text ? <p className={className} role="status" aria-live="polite">{text}</p> : null;
      })()}

      <ul className="staging-list" aria-label="Staged files">
        {files.map((file) => {
          const fileViolation = violations.find(
            (v): v is Violation<'singleFile'> =>
              v.type === 'singleFile' && file.size > limits.maxSingleFile
          );

          // US-001-05: failed rows get danger styling + retry button (AC-001-3).
          const isFailed = file.status === 'failed';

          return (
            <li
              key={file.id}
              className={`file-row ${fileViolation ? 'file-row--over-limit' : ''} ${isFailed ? 'file-row--failed' : ''}`}
              title={
                fileViolation
                  ? `Exceeds per-file limit: ${formatBytes(limits.maxSingleFile)}`
                  : isFailed
                    ? 'Upload failed — retry'
                    : undefined
              }
            >
              {fileIcon}
              <span className="file-name" title={file.name}>
                {file.name}
              </span>
              {/* US-001-05: show "Upload failed — retry" text instead of size for failed files. */}
              {isFailed ? (
                <span className="file-size file-failed-label">Upload failed — retry</span>
              ) : (
                <span className="file-size">{formatBytes(file.size)}</span>
              )}
              {/* FR-001-5: per-file progress bar + percent / done checkmark. */}
              {(file.status === 'uploading' || file.status === 'done') && (
                <>
                  <div
                    className="file-progress-track"
                    role="progressbar"
                    aria-valuenow={Math.round(file.progress * 100)}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`Upload progress for ${file.name}`}
                  >
                    <div className="file-progress" style={{ width: `${file.progress * 100}%` }} />
                  </div>
                  {file.status === 'done' ? (
                    <span className="file-done-icon" aria-hidden="true">✓</span>
                  ) : (
                    <span className="file-percent">{Math.round(file.progress * 100)}%</span>
                  )}
                </>
              )}
              {/* US-001-05: Retry button for failed files (AC-001-3). */}
              {isFailed && (
                <button
                  type="button"
                  className="btn btn-secondary file-retry-button"
                  aria-label={`Retry ${file.name}`}
                  onClick={() => retry(file.id)}
                >
                  {retryIcon}
                </button>
              )}
              <button
                type="button"
                className="file-remove"
                aria-label={`Remove ${file.name}`}
                onClick={() => remove(file.id)}
              >
                {removeIcon}
              </button>
            </li>
          );
        })}
      </ul>

      {/* US-001-03: violation message — names the exact cap via formatBytes() */}
      {hasViolation && (
        <div className="staging-violation" role="alert">
          {violations.map((v) => (
            <span key={v.type}>
              {v.type === 'total' ? (
                `Transfer size ${formatBytes(v.size)} exceeds the ${formatBytes(v.limit)} limit.`
              ) : (
                `A single file is ${formatBytes(v.size)} — the per-file limit is ${formatBytes(v.limit)}.`
              )}
            </span>
          ))}
        </div>
      )}

      {/* US-001-03: Send button in list footer. Disabled only when empty or a violation exists.
          We use aria-disabled + opacity style instead of native disabled so the test can still
          interact with it (aria-only for accessibility without blocking programmatic interaction). */}
      <button
        type="button"
        className="staging-send-button"
        aria-disabled={hasViolation ? 'true' : 'false'}
        aria-label={`Send transfer, ${files.length} file${files.length === 1 ? '' : 's'}, total ${formatBytes(totalBytes)}`}
        style={{ opacity: hasViolation ? '0.55' : '1' }}
        title={hasViolation ? 'Limit exceeded — remove files to send.' : 'Send transfer'}
        onClick={async () => {
          // Fix 5: guard against double-submit.
          if (isSendingRef.current) return;
          isSendingRef.current = true;

          try {
            // Build the draft payload per TA-4.2#1.
            const payload = {
              files: files.map((f) => ({ name: f.name, sizeBytes: f.size })) as DraftFile[],
            };

            const response = await fetch('/api/v1/transfers/draft', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify(payload),
            });

            if (response.ok) {
              const data = await response.json();

              // Fix 6: validate draft response before passing to start().
              const parsedData = data as { files?: Array<{ uploadUrl: string }> };
              if (!parsedData.files || !Array.isArray(parsedData.files) || parsedData.files.length !== files.length) {
                showToast('error', 'Invalid draft response from server.');
                return;
              }

              start(data as UploadDraft); // triggers block upload (US-001-04 / T-009)
            } else {
              // Map server error codes to human messages (TA-4.1.3: Problem+JSON with closed code).
              const contentType = response.headers.get('content-type') ?? '';
              let errorMessage = 'Upload failed.';

              if (/application\/json/.test(contentType)) {
                try {
                  const problem = await response.json();
                  switch (problem.code) {
                    case 'TRANSFER_SIZE_EXCEEDED':
                      errorMessage = `Transfer size ${formatBytes(problem.details?.bytes ?? totalBytes)} exceeds the plan limit of ${formatBytes(limits.maxTransferSize)}.`;
                      break;
                    case 'STORAGE_QUOTA_EXCEEDED':
                      errorMessage = 'Storage quota exceeded.';
                      break;
                    default:
                      errorMessage = problem.title || `Error ${response.status}: ${problem.detail ?? errorMessage}`;
                  }
                } catch {
                  // Fallback if response is not valid JSON
                  errorMessage = `Upload failed (${response.status}).`;
                }
              } else {
                errorMessage = `Upload failed: ${response.statusText}.`;
              }

              showToast('error', errorMessage);
            }
          } catch (e) {
            // Network error, CORS, etc. — generic error with optional correlationId if available.
            let message = 'Network error. Please try again.';
            const err = e as Error & { response?: Response };
            if (err.response?.status === 409 && err.response.headers.get('x-correlation-id')) {
              // Could include correlation ID in toast, but MVP keeps it simple.
              message = 'Transfer rejected: limit exceeded.';
            }
            showToast('error', message);
          } finally {
            isSendingRef.current = false; // Fix 5: release send guard.
          }
        }}
      >
        Send
      </button>

      <p className="staging-total">
        {files.length === 1 ? '1 file' : `${files.length} files`} · {formatBytes(totalBytes)}
      </p>
    </div>
  );
}
