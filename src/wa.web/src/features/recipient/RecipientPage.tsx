import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import { useRecipientStore } from './RecipientStore'

interface RecipientPageProps {
  linkId: string
}

/**
 * T-003-02 (FR-003-1/2, AC-003-1 client half): recipient page at /t/{linkId}.
 * loading → error → active rendering. Active screen: "From: {senderName}", optional note as a blockquote,
 * file rows (icon + name + human size via formatBytes) each with a Download button whose aria-label is the
 * exact exit-check string "Download {name}, {size}"; "Downloads left: N" under the list for finite caps (T-003-03, FR-003-5).
 * T-003-03 (FR-003-8/9): expired and download-limit render one shared terminal screen (icon, one line from the
 * store's Problem+JSON details, one action) — no file list, no sizes; 'expired' covers both 410 and 404 so those
 * screens stay byte-identical by construction. The "Send something" anchor is a placeholder — T-003-07 wires it.
 * T-003-05 (FR-003-6, AC-003-2 client half): when passwordRequired === true the ACTIVE screen renders ONLY the
 * centered password card — no file list, no sizes, nothing beyond the sender name. Submit → endpoint 5; correct
 * entry stores the JWT in sessionStorage and re-queries with ?t= (no re-prompt on same-tab refresh); wrong entry
 * shakes + role=alert "Wrong password" while staying gated. Terminal states always take precedence over the gate —
 * the T-003-03 shared screen wins whenever status !== 'active' (T-003-04 emits terminal codes over the wire).
 * **No sign-up wall** — FR-003-2.
 * The linkId arrives by prop from App (recipientRoute.ts matches the pathname — no window here).
 */
export function RecipientPage({ linkId }: RecipientPageProps) {
  const status = useRecipientStore((s) => s.status)
  const from = useRecipientStore((s) => s.from)
  const note = useRecipientStore((s) => s.note)
  const files = useRecipientStore((s) => s.files)
  const hasDownloadAll = useRecipientStore((s) => s.hasDownloadAll)
  const downloadsLeft = useRecipientStore((s) => s.downloadsLeft)
  const terminalMessage = useRecipientStore((s) => s.terminalMessage)
  const passwordRequired = useRecipientStore((s) => s.passwordRequired)
  const unlocking = useRecipientStore((s) => s.unlocking)
  const unlockError = useRecipientStore((s) => s.unlockError)
  // T-003-06: per-row download indicator (UI notes: subtle progress/indicator; no page-level blocking UI).
  const downloadingFileId = useRecipientStore((s) => s.downloadingFileId)
  const downloadErrorFileId = useRecipientStore((s) => s.downloadErrorFileId)

  // T-003-05 (FR-003-6): gate-local state — the password value resets on unmount/remount by construction.
  const [value, setValue] = useState('')
  const [show, setShow] = useState(false)

  // T-003-02: mount-time load; reset on unmount keeps tests hermetic (UploadEngine's test-cleanup convention).
  useEffect(() => {
    void useRecipientStore.getState().load(linkId)
    return () => useRecipientStore.getState().reset()
  }, [linkId])

  // T-003-06 (AC-003-1 client half): real download wiring — per-row Download button calls endpoint 6.
  const handleDownload = (fileId: string) => {
    void useRecipientStore.getState().download(linkId, fileId)
  }
  const handleDownloadAll = () => {}

  // T-003-05 (AC-003-2): submit → endpoint 5; the store owns token storage + the ?t= re-query. Never rejects.
  const handleSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    void useRecipientStore.getState().unlock(linkId, value)
  }

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

  if (status === 'expired' || status === 'downloadLimit') {
    // T-003-03 (FR-003-8/9, UI-Reference §4.6): one shared terminal screen — icon, one line (server Problem+JSON
    // details verbatim), one action. Expired vs download-limit differ only in the stored message; the 404 and
    // email-less 410 bodies are both email-less, so their screens stay byte-identical by construction.
    // T-003-05: this branch also wins over a gated transfer — terminal states take precedence over the password gate.
    return (
      <section className="rp rp-terminal" aria-label="Transfer unavailable">
        <span className="rp-icon rp-terminal-icon" aria-hidden="true">
          {/* Same inline file icon as the rows below — TA-8.2: features self-contained. */}
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9Z" />
            <path d="M14 3v6h5" />
          </svg>
        </span>
        <h1 className="rp-heading">{terminalMessage}</h1>
        {/* T-003-07 (growth loop) wires this CTA — placeholder anchor to / at this task. */}
        <a className="btn-primary rp-terminal-action" href="/">Send something</a>
      </section>
    )
  }

  if (status === 'active' && passwordRequired) {
    // T-003-05 (FR-003-6): the gate — the ONLY content on a gated transfer is this card: sender name + form.
    // No note, no file list, no sizes, nothing to enumerate or leak (byte-leak audit in RecipientPage.test.tsx).
    return (
      <section className="rp rp-gate" aria-label={`From ${from} — password required`}>
        <h1 className="rp-heading">From: {from}</h1>
        <form className="rp-password-card" onSubmit={handleSubmit}>
          <label className="rp-password-label" htmlFor="rp-password-input">Password</label>
          <div className={`rp-password-row${unlockError ? ' rp-shake' : ''}`}>
            <input
              id="rp-password-input"
              type={show ? 'text' : 'password'}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              className="rp-password-input"
              autoComplete="current-password"
            />
            <button
              type="button"
              className="rp-password-toggle"
              aria-label={show ? 'Hide password' : 'Show password'}
              aria-pressed={show}
              onClick={() => setShow((s) => !s)}
            >
              {show ? (
                // feather eye-off — inline, features self-contained (TA-8.2).
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" />
                  <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
                  <path d="M14.12 14.12A3 3 0 1 1 10.9 10.9" />
                  <line x1="1" y1="1" x2="23" y2="23" />
                </svg>
              ) : (
                // feather eye — inline, features self-contained (TA-8.2).
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                  <circle cx="12" cy="12" r="3" />
                </svg>
              )}
            </button>
          </div>
          {unlockError && (
            <p className="rp-password-error" role="alert">{unlockError}</p>
          )}
          <button type="submit" className="btn-primary rp-password-submit" disabled={unlocking || value.length === 0}>
            Unlock
          </button>
        </form>
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
            {/* T-003-06 (US-003-03 alt flow): storage degraded — the row's mint failed; the rest of the page keeps working. */}
            {downloadErrorFileId === f.fileId && (
              <p className="rp-download-error" role="alert">File temporarily unavailable.</p>
            )}
            <span className="rp-size">{formatBytes(f.sizeBytes)}</span>
            {/* T-003-06 (UI notes): subtle in-flight indicator on the row while endpoint 6 is minting — no page-level blocking UI. */}
            <button type="button" className="rp-download" disabled={downloadingFileId !== null} aria-label={`Download ${f.name}, ${formatBytes(f.sizeBytes)}`} onClick={() => handleDownload(f.fileId)}>
              {downloadingFileId === f.fileId ? 'Downloading…' : 'Download'}
            </button>
          </li>
        ))}
      </ul>
      {/* FR-003-1: Download all when >1 file — hasDownloadAll comes from the server (TA-4.2#4), so the client never recomputes it. */}
      {hasDownloadAll && (
        <button type="button" className="btn-primary rp-download-all" onClick={handleDownloadAll}>Download all</button>
      )}
      {/* T-003-03 (FR-003-5): "Downloads left: N" under the list — null = unlimited (Business, hidden);
      * shown only for finite caps past the first download (a 0 remaining is already the terminal limit screen). */}
      {downloadsLeft !== null && downloadsLeft > 0 && (
        <p className="rp-downloads-left">Downloads left: {downloadsLeft}</p>
      )}
    </section>
  )
}

export default RecipientPage
