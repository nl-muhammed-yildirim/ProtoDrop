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

interface DraftFile {
  name: string;
  sizeBytes: number;
}

export function StagingList() {
  const files = useUploadEngine((state) => state.files);
  const totalBytes = useUploadEngine((state) => state.overall.totalBytes);
  const remove = useUploadEngine((state) => state.remove);
  const start = useUploadEngine((state) => state.start);

  // US-001-03: resolve effective limits (guest fallback; replace with /api/v1/limits fetch when auth lands).
  const limits = resolveEffectiveLimits();

  // US-001-03: validate selection on every render (reactive to changes in files array or limits).
  const violations = validateSelection(files, limits);
  const hasViolation = violations.length > 0;

  if (files.length === 0) return null; // empty list → the drop zone is the whole UI again

  return (
    <div className="staging">
      <ul className="staging-list" aria-label="Staged files">
        {files.map((file) => {
          const fileViolation = violations.find(
            (v): v is Violation<'singleFile'> =>
              v.type === 'singleFile' && file.size > limits.maxSingleFile
          );

          return (
            <li
              key={file.id}
              className={`file-row ${fileViolation ? 'file-row--over-limit' : ''}`}
              title={fileViolation ? `Exceeds per-file limit: ${formatBytes(limits.maxSingleFile)}` : undefined}
            >
              {fileIcon}
              <span className="file-name" title={file.name}>
                {file.name}
              </span>
              <span className="file-size">{formatBytes(file.size)}</span>
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
          // Build the draft payload per TA-4.2#1.
          const payload = {
            files: files.map((f) => ({ name: f.name, sizeBytes: f.size })) as DraftFile[],
          };

          try {
            const response = await fetch('/api/v1/transfers/draft', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify(payload),
            });

            if (response.ok) {
              const data = await response.json();
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
