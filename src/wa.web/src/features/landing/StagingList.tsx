import { useUploadEngine } from '../../core/upload/UploadEngine'
import { formatBytes } from '../../core/upload/formatBytes'
import { checkLimits, getPlanLimits, limitMessage } from '../../core/upload/planLimits'

/**
 * Staging list (T-001-03): one row per staged file — icon, name (truncated, `title`),
 * size via `formatBytes`, remove ✕; total line "N files · X" recomputed from store
 * state on every add/remove. **Send.** is enabled only while the list is non-empty
 * and no limit violation stands (T-001-04): oversized rows are marked, a message
 * names the exact limit under the total, and Send.'s tooltip explains why it's off.
 * T-001-07 adds upload progress: an overall "Uploading… X of Y" line above the list
 * (the polite status region) plus per-row 3 px bar + percentage for rows that are
 * uploading or done, and a `--success` check on completed rows — all read live from
 * the store, no polling. T-001-08 adds row-local failure (FR-001-6): rows whose file failed
 * all per-block retries turn `--danger` — name + "Upload failed — retry" text and a Retry button
 * that re-enters the engine from the last completed block. The component stays render-only; Send.'s wiring is T-001-10.
 */

export function StagingList() {
  const files = useUploadEngine((s) => s.files)
  const overall = useUploadEngine((s) => s.overall)
  const removeFile = useUploadEngine((s) => s.remove)
  const retryFile = useUploadEngine((s) => s.retry)

  // Pure arithmetic on staged sizes; re-read every render (TA-8.3).
  const limits = getPlanLimits()
  const check = checkLimits(files, limits)
  const message = limitMessage(files, check, limits)

  return (
    <section className="sl" aria-label="Staged files">
      {/* Overall line (FR-001-5): combined bytes uploaded / total, recomputed on every store transition. */}
      {/* role=status (polite): the text re-renders only on store transitions — block completions / status changes — never per byte (F-TRF-016-3). */}
      {files.length > 0 && (
        <p className="sl-overall" role="status">Uploading… {formatBytes(overall.sentBytes)} of {formatBytes(overall.totalBytes)}</p>
      )}
      <ul className="sl-list">
        {files.map((f) => (
          <li key={f.id} className={`sl-row${check.overFileIds.has(f.id) ? ' sl-row-over' : ''}${f.status === 'failed' ? ' sl-row-failed' : ''}`}>
            <span className="sl-icon" aria-hidden="true">
              {/* generic file icon */}
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9Z" />
                <path d="M14 3v6h5" />
              </svg>
            </span>
            <span className="sl-name" title={f.name}>{f.name}</span>
            <span className="sl-size">{formatBytes(f.size)}</span>
            {(f.status === 'uploading' || f.status === 'done') && (
              <>
                <span className="sl-percent">{Math.round(f.progress * 100)}%</span>
                {/* 3 px bar pinned to the row's bottom edge; aria-hidden — the percent span carries the value. */}
                <div className="sl-progress-track" aria-hidden="true">
                  <div className="sl-bar" style={{ width: `${f.progress * 100}%` }} />
                </div>
              </>
            )}
            {f.status === 'done' && <span className="sl-done" aria-hidden="true">✓</span>}
            {/* FR-001-6 (T-001-08): row-local failure — danger text + Retry; the engine resumes from completed blocks. */}
            {f.status === 'failed' && (
              <>
                <span className="sl-failed-msg">Upload failed — retry</span>
                <button type="button" className="sl-retry" aria-label={`Retry ${f.name}`} onClick={() => void retryFile(f.id)}>Retry.</button>
              </>
            )}
            <button
              type="button"
              className="sl-remove"
              aria-label={`Remove ${f.name}`}
              onClick={() => removeFile(f.id)}
            >✕</button>
          </li>
        ))}
      </ul>
      <p className="sl-total">{files.length} {files.length === 1 ? 'file' : 'files'} · {formatBytes(overall.totalBytes)}</p>
      {message && (
        <p className="sl-limit-msg" role="alert">
          {message}
        </p>
      )}
      <div className="sl-footer">
        {/* Disabled buttons don't fire their own mouse events — the span wrapper carries title. */}
        <span className="sl-send-wrap" title={message ?? undefined}>
          <button type="button" className="btn-primary sl-send" disabled={files.length === 0 || message !== null}>Send.</button>
        </span>
      </div>
    </section>
  )
}

export default StagingList
