import { useEffect, useRef, useState } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import type { ListTransfersResponse } from './formatting'
import {
  downloadsLabel,
  formatRemaining,
  recipientLabel,
  statusChip,
} from './formatting'

interface FilesPageProps {
  onNewTransfer: () => void   // App owns navigation to "/" (goHome precedent — T-008-07)
}

// T-009-05: fixed local port, no proxy, no env vars yet (AuthPage API_BASE line-14 precedent).
const API_BASE = 'http://localhost:8080/api/v1'

/**
 * T-009-05 (UI-Reference §5.4): My Files screen at /files (signed-in only — App guards the branch).
 * Cursor-paged transfer history from endpoint 14 (GET /api/v1/transfers?status=&cursor=, TA-4.2#14),
 * fixed server sort CreatedAtUtc DESC (TA-4.1.4). Filter pills All/Active/Expired forward the pill's
 * wire value to ?status= — the client NEVER re-filters rows (US-009-02 canonical mapping is server-side).
 * One 1s setInterval tick (AuthPage cooldown-interval precedent) drives every row's countdown — there
 * is exactly ONE interval for the whole list and no other timer/polling anywhere.
 */
export function FilesPage({ onNewTransfer }: FilesPageProps) {
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'expired'>('all')
  const [rows, setRows] = useState<ListTransfersResponse['items']>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [nowMs, setNowMs] = useState(() => Date.now())

  // Refs are the source of truth for paging (read inside fetch closures without re-running them).
  // cursorsRef[i] is the opaque cursor used to load page i+1; page 0 is always null.
  const cursorsRef = useRef<(string | null)[]>([null])
  const filterRef = useRef<'all' | 'active' | 'expired'>('all')

  // pageIndex/hasNext are mirrored to state purely to render the Prev/Next disabled states.
  const [pageIndex, setPageIndex] = useState(0)
  const [hasNext, setHasNext] = useState(false)

  // AC-009-3 client half: single 1s tick drives every countdown; no per-second server calls.
  useEffect(() => {
    const id = window.setInterval(() => setNowMs(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  // Fetch page 0 on mount and whenever the pill changes (resets cursors + index — US-009-02 happy path 3).
  useEffect(() => {
    filterRef.current = statusFilter
    cursorsRef.current = [null]
    setLoading(true)
    setError(null)

    // limit 25 is the endpoint's default (TA-4.1.4) — do NOT send it; keep the query minimal like the rest of the client.
    fetch(`${API_BASE}/transfers?status=${statusFilter}`, { credentials: 'include' })
      .then(async (res) => {
        if (!res.ok) throw new Error('Could not load your files.')
        return (await res.json()) as ListTransfersResponse
      })
      .then((data) => {
        // Record page 1's nextCursor so Next can walk to it.
        cursorsRef.current[1] = data.nextCursor
        applyPage(data, 0)
      })
      .catch(() => setError('Could not load your files.'))
      .finally(() => setLoading(false))
  }, [statusFilter])

  function applyPage(data: ListTransfersResponse, index: number): void {
    setRows(data.items ?? [])
    setPageIndex(index)
    setHasNext(data.nextCursor !== null)   // Next enabled only when the current page still has a next.
  }

  async function goToPage(index: number) {
    if (index < 0 || index >= cursorsRef.current.length) return
    const cursor = cursorsRef.current[index]   // null for page 0, opaque for later pages
    setLoading(true)
    setError(null)

    let url = `${API_BASE}/transfers?status=${filterRef.current}`
    if (cursor !== null) url += `&cursor=${encodeURIComponent(cursor)}`

    try {
      const res = await fetch(url, { credentials: 'include' })
      if (!res.ok) throw new Error('Could not load your files.')
      const data = (await res.json()) as ListTransfersResponse
      // TA-4.1.4: the returned nextCursor becomes the cursor for the NEXT page past this one.
      cursorsRef.current[index + 1] = data.nextCursor
      applyPage(data, index)   // each response REPLACES rows — one full screen of 25 (UI-Reference §5.4).
    } catch {
      setError('Could not load your files.')
    } finally {
      setLoading(false)
    }
  }

  const pills = ['all', 'active', 'expired'] as const
  const prevDisabled = pageIndex === 0

  return (
    <div className="mf">
      <div className="mf-header">
        <h1 className="mf-h1">My Files</h1>
        <button type="button" className="mf-new-btn" onClick={onNewTransfer}>New transfer</button>
      </div>

      <div className="mf-pills">
        {pills.map((p) => (
          <button
            key={p}
            type="button"
            className={`mf-pill${statusFilter === p ? ' mf-pill-active' : ''}`}
            aria-pressed={statusFilter === p}
            onClick={() => setStatusFilter(p)}
          >
            {p === 'all' ? 'All' : p === 'active' ? 'Active' : 'Expired'}
          </button>
        ))}
      </div>

      {loading && <p className="mf-loading">Loading…</p>}

      {!loading && error && (
        <p role="alert" className="mf-error">
          {error}
          {' '}
          <button type="button" className="mf-retry" onClick={() => void goToPage(pageIndex)}>Try again</button>
        </p>
      )}

      {!loading && !error && rows.length > 0 && (
        <>
          <ul className="mf-list">
            {rows.map((row) => {
              const chip = statusChip(row.status)
              const remaining = formatRemaining(row.expiresAtUtc, nowMs)
              return (
                <li key={row.id} className="mf-row">
                  <span className="mf-name" title={row.name}>{row.name}</span>
                  <span className="mf-meta">
                    {row.fileCount === 1 ? '1 file' : `${row.fileCount} files`} · {formatBytes(row.totalBytes)} ·{' '}
                    {recipientLabel(row.recipientCount)} · {downloadsLabel(row.downloadsCount, row.maxDownloads)}
                  </span>
                  <span className={`mf-chip mf-chip-${chip.tone}`}>{chip.text}</span>
                  <span className="mf-countdown">{remaining.text}</span>
                </li>
              )
            })}
          </ul>

          <div className="mf-paging">
            <button type="button" className="mf-prev" disabled={prevDisabled} onClick={() => void goToPage(pageIndex - 1)}>
              Prev
            </button>
            <button type="button" className="mf-next" disabled={!hasNext} onClick={() => void goToPage(pageIndex + 1)}>
              Next
            </button>
          </div>
        </>
      )}

      {/* FR-009-5 empty state: rows.length === 0 && !loading && !error */}
      {!loading && !error && rows.length === 0 && (
        <div className="mf-empty">
          <div className="mf-empty-icon" aria-hidden="true">📁</div>
          <p className="mf-empty-text">No transfers yet.</p>
          <button type="button" className="mf-new-btn mf-empty-cta" onClick={onNewTransfer}>Send something</button>
        </div>
      )}
    </div>
  )
}

export default FilesPage
