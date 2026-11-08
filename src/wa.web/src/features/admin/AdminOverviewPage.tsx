import { useEffect, useState } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import { ADMIN_TRANSFERS_PATH } from './adminRoute'
import type { OverviewDto } from './overviewData'
import { formatUpdatedAt, statusLabel, statusTone } from './overviewData'
import { getTraceParent } from '../../core/traceparent'

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AuthPage + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; data: OverviewDto }

/**
 * T-011-10 (FR-011-2/6): admin Overview screen — four stat cards + "Updated {time}" line + read-only
 * recent transfers table. Data comes from endpoint 20 (GET /admin/overview) fetched once on mount;
 * the 5-min staleness bound is server-side (T-011-03 cache) — the client states snapshotAtUtc, no math.
 */
export function AdminOverviewPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  useEffect(() => {
    const load = async () => {
      try {
        const res = await fetch(`${API_BASE}/admin/overview`, { credentials: 'include', headers: { traceparent: getTraceParent() } })
        if (!res.ok) {
          setState({ kind: 'error', message: 'Failed to load overview. Please try again.' })
          return
        }
        const data = (await res.json()) as OverviewDto
        setState({ kind: 'ready', data })
      } catch {
        setState({ kind: 'error', message: 'Failed to load overview. Please try again.' })
      }
    }
    void load()
  }, [])

  const navigate = (path: string) => {
    window.history.pushState('', '', path)
    window.dispatchEvent(new PopStateEvent('popstate')) // App's popstate listener updates pathname.
  }

  if (state.kind === 'loading') {
    return <div className="ad-loading">Loading overview…</div>
  }

  if (state.kind === 'error') {
    return (
      <div className="mf-error" role="alert">
        {state.message}{' '}
        <button type="button" className="mf-retry" onClick={() => setState({ kind: 'loading' })}>
          Retry
        </button>
      </div>
    )
  }

  const { data } = state

  return (
    <section className="ad-overview" aria-label="Admin overview">
      <h1 className="mf-h1">Overview</h1>

      <div className="ad-cards">
        <div className="ad-card">
          <div className="ad-card-value">{data.activeTransfers}</div>
          <div className="ad-card-label">Active transfers</div>
        </div>
        <div className="ad-card">
          <div className="ad-card-value">{formatBytes(data.storageBytesUsed)}</div>
          <div className="ad-card-label">Storage used</div>
        </div>
        <div className="ad-card">
          <div className="ad-card-value">{data.newUsersToday}</div>
          <div className="ad-card-label">New users today</div>
        </div>
        <div className="ad-card">
          <div className="ad-card-value">
            {data.emailsSent7d}
            <span className={data.emailsFailed7d > 0 ? ' ad-danger' : ''}> / {data.emailsFailed7d}</span>
          </div>
          <div className="ad-card-label">Emails sent / failed (7d)</div>
        </div>
      </div>

      {/* FR-011-6: staleness stated on screen — the server's snapshot time, no client computation. */}
      <p className="ad-updated">Updated {formatUpdatedAt(data.snapshotAtUtc)}</p>

      <button type="button" className="au-ghost-btn" onClick={() => navigate(ADMIN_TRANSFERS_PATH)}>
        View all
      </button>

      <table className="ad-table">
        <thead>
          <tr>
            <th>Link</th>
            <th>Owner</th>
            <th>Status</th>
            <th>Size</th>
            <th>Created</th>
          </tr>
        </thead>
        <tbody>
          {data.recentTransfers.map((row) => (
            <tr key={row.linkId}>
              <td className="ad-linkid">{row.linkId}</td>
              <td>{row.ownerEmail ?? '—'}</td>
              <td>
                <span className={`ad-chip ad-chip-${statusTone(row.status)}`}>{statusLabel(row.status)}</span>
              </td>
              <td>{formatBytes(row.totalBytes)}</td>
              <td>{formatUpdatedAt(row.createdAtUtc)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}

export default AdminOverviewPage
