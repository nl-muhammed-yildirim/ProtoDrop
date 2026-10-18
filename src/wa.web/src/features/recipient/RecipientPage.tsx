import { useEffect } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import { useRecipientStore } from './RecipientStore'

interface RecipientPageProps {
  linkId: string
}

/**
 * T-003-02 (FR-003-1/2, AC-003-1 client half): recipient page at /t/{linkId}.
 * loading → error → active rendering. Active screen: "From: {senderName}", optional note as a blockquote,
 * file rows (icon + name + human size via formatBytes) each with a Download button whose aria-label is the
 * exact exit-check string "Download {name}, {size}". **No sign-up wall** — FR-003-2.
 * The linkId arrives by prop from App (recipientRoute.ts matches the pathname — no window here).
 */
export function RecipientPage({ linkId }: RecipientPageProps) {
  const status = useRecipientStore((s) => s.status)
  const from = useRecipientStore((s) => s.from)
  const note = useRecipientStore((s) => s.note)
  const files = useRecipientStore((s) => s.files)
  const hasDownloadAll = useRecipientStore((s) => s.hasDownloadAll)

  // T-003-02: mount-time load; reset on unmount keeps tests hermetic (UploadEngine's test-cleanup convention).
  useEffect(() => {
    void useRecipientStore.getState().load(linkId)
    return () => useRecipientStore.getState().reset()
  }, [linkId])

  // T-003-06 / F-TRF-004 (T-017): real download wiring arrives later — the page view itself must not mint SAS (TA-4.2#4).
  const handleDownload = () => {}
  const handleDownloadAll = () => {}

  if (status === 'loading') {
    return <p className="rp-loading" role="status">Loading…</p>
  }

  if (status === 'error') {
    return (
      <section className="rp" aria-label="Transfer unavailable">
        <h1 className="rp-heading rp-error">Something went wrong</h1>
        <p className="rp-sub">Please try again later.</p>
      </section>
    )
  }

  return (
    <section className="rp" aria-label={`Files from ${from}`}>
      <h1 className="rp-heading">From: {from}</h1>
      {/* Note as plain-text blockquote — no markdown (TA-8.2); overflow-wrap handles long unbroken URLs. */}
      {note && note.trim() !== '' && (
        <blockquote className="rp-note">{note}</blockquote>
      )}
      <ul className="rp-list" aria-label="Files">
        {files.map((f) => (
          <li key={f.fileId} className="rp-row">
            <span className="rp-icon" aria-hidden="true">
              {/* Inline copy of StagingList's generic file icon — TA-8.2: features self-contained. */}
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9Z" />
                <path d="M14 3v6h5" />
              </svg>
            </span>
            <span className="rp-name" title={f.name}>{f.name}</span>
            <span className="rp-size">{formatBytes(f.sizeBytes)}</span>
            <button type="button" className="rp-download" aria-label={`Download ${f.name}, ${formatBytes(f.sizeBytes)}`} onClick={handleDownload}>
              Download
            </button>
          </li>
        ))}
      </ul>
      {/* FR-003-1: Download all when >1 file — hasDownloadAll comes from the server (TA-4.2#4), so the client never recomputes it. */}
      {hasDownloadAll && (
        <button type="button" className="btn-primary rp-download-all" onClick={handleDownloadAll}>Download all</button>
      )}
    </section>
  )
}

export default RecipientPage
