import { useEffect } from 'react';
import { formatBytes } from '../../core/upload/formatBytes';
import { useRecipientStore } from './RecipientStore';

// US-003-01: Recipient page — renders the file list for a public transfer link.
// No sign-up wall (FR-003-2). Self-contained feature module (TA-8.2).

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

export function RecipientPage() {
  const status = useRecipientStore((state) => state.status);
  const from = useRecipientStore((state) => state.from);
  const note = useRecipientStore((state) => state.note);
  const files = useRecipientStore((state) => state.files);
  const hasDownloadAll = useRecipientStore((state) => state.hasDownloadAll);
  const error = useRecipientStore((state) => state.error);
  const load = useRecipientStore((state) => state.load);

  // US-003-01: read linkId from URL path on mount and call load().
  useEffect(() => {
    const match = window.location.pathname.match(/t\/([A-Z0-9]{8})/);
    if (match?.[1]) {
      void load(match[1]);
    }
  }, [load]);

  if (status === 'loading') {
    return <p className="recipient-loading">Loading…</p>;
  }

  if (status === 'error') {
    return (
      <div className="recipient-error-card">
        <h2>Something went wrong</h2>
        <p>{error}</p>
      </div>
    );
  }

  return (
    <div className="recipient-page">
      <h1 className="recipient-header">From: {from}</h1>
      {note && <blockquote className="recipient-note">{note}</blockquote>}
      <ul className="recipient-file-list" aria-label="Files">
        {files.map((file) => (
          <li key={file.fileId} className="recipient-file-row">
            {fileIcon}
            <span className="file-name" title={file.name}>{file.name}</span>
            <span className="file-size">{formatBytes(file.sizeBytes)}</span>
            <button
              type="button"
              className="btn btn-secondary file-download-btn"
              aria-label={`Download ${file.name}`}
            >
              Download
            </button>
          </li>
        ))}
      </ul>
      {hasDownloadAll && (
        <button type="button" className="btn btn-primary recipient-download-all-btn">
          Download all
        </button>
      )}
    </div>
  );
}
