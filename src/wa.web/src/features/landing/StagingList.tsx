import { useUploadEngine } from '../../core/upload/UploadEngine'
import { formatBytes } from '../../core/upload/formatBytes'

/**
 * Staging list (T-001-03): one row per staged file — icon, name (truncated, `title`),
 * size via `formatBytes`, remove ✕; total line "N files · X" recomputed from store
 * state on every add/remove; footer **Send.** enabled only while the list is non-empty.
 */

export function StagingList() {
  const files = useUploadEngine((s) => s.files)
  const overall = useUploadEngine((s) => s.overall)
  const removeFile = useUploadEngine((s) => s.remove)

  return (
    <section className="sl" aria-label="Staged files">
      <ul className="sl-list">
        {files.map((f) => (
          <li key={f.id} className="sl-row">
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
      <div className="sl-footer">
        <button type="button" className="btn-primary sl-send" disabled={files.length === 0}>Send.</button>
      </div>
    </section>
  )
}

export default StagingList
