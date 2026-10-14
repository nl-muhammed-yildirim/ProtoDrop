import { useUploadEngine } from '../../core/upload/UploadEngine'
import { formatBytes } from '../../core/upload/formatBytes'
import { checkLimits, getPlanLimits, limitMessage } from '../../core/upload/planLimits'

/**
 * Staging list (T-001-03): one row per staged file — icon, name (truncated, `title`),
 * size via `formatBytes`, remove ✕; total line "N files · X" recomputed from store
 * state on every add/remove. **Send.** is enabled only while the list is non-empty
 * and no limit violation stands (T-001-04): oversized rows are marked, a message
 * names the exact limit under the total, and Send.'s tooltip explains why it's off.
 */

export function StagingList() {
  const files = useUploadEngine((s) => s.files)
  const overall = useUploadEngine((s) => s.overall)
  const removeFile = useUploadEngine((s) => s.remove)

  // Pure arithmetic on staged sizes; re-read every render (TA-8.3).
  const limits = getPlanLimits()
  const check = checkLimits(files, limits)
  const message = limitMessage(files, check, limits)

  return (
    <section className="sl" aria-label="Staged files">
      <ul className="sl-list">
        {files.map((f) => (
          <li key={f.id} className={`sl-row${check.overFileIds.has(f.id) ? ' sl-row-over' : ''}`}>
            <span className="sl-icon" aria-hidden="true">
              {/* generic file icon */}
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9Z" />
                <path d="M14 3v6h5" />
              </svg>
            </span>
            <span className="sl-name" title={f.name}>{f.name}</span>
            <span className="sl-size">{formatBytes(f.size)}</span>
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
