import { formatBytes } from '../../core/upload/formatBytes';
import { useUploadEngine } from '../../core/upload/UploadEngine';

// US-001-02 / UI-Reference §4.2 — file rows: min-height 44 px, icon, name (truncated,
// title attr), size in --fs-small/--fg-muted, remove ✕ while staging. Total line:
// "N files · X GB" in --fs-small/--fg-muted.

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

export function StagingList() {
  const files = useUploadEngine((state) => state.files);
  const totalBytes = useUploadEngine((state) => state.overall.totalBytes);
  const remove = useUploadEngine((state) => state.remove);

  if (files.length === 0) return null; // empty list → the drop zone is the whole UI again

  return (
    <div className="staging">
      <ul className="staging-list" aria-label="Staged files">
        {files.map((file) => (
          <li key={file.id} className="file-row">
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
        ))}
      </ul>
      <p className="staging-total">
        {files.length === 1 ? '1 file' : `${files.length} files`} · {formatBytes(totalBytes)}
      </p>
    </div>
  );
}
