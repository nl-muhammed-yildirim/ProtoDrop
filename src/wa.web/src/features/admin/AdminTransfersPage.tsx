import { useEffect, useRef, useState } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import { statusLabel, statusTone } from './overviewData'
import type { AdminTransferRowDto, SearchParams } from './transfersData'
import { buildSearchQuery, formatCreatedAt, parseSearchResponse } from './transfersData'
import { getTraceParent } from '../../core/traceparent'

interface AdminTransfersPageProps {
  role: 'SuperAdmin' | 'Operator'   // T-011-10 precedent — AdminShell narrows /auth/me's role and passes it down.
}

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AdminShell + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

// FR-011-3 status vocabulary (TA-4.2#21) — the wire words, verbatim; absent → all.
const STATUS_OPTIONS = ['all', 'draft', 'active', 'expired', 'download_limit', 'deleted'] as const

/**
 * T-011-11 (FR-011-3 / FR-011-4): admin Transfers screen at /admin/transfers — endpoint 21.
 * Filter bar: ONE q box (server OR = LinkId exact CI OR EmailRecipient LIKE %q%), status select,
 * size range (minBytes/maxBytes pair), date range (from/to pair). Pairs are only sent when BOTH are
 * filled — the server rejects an unpaired range with 400 VALIDATION. Cursor pagination at 25 rows
 * (TA-4.1.4): Next/Previous walk the opaque nextCursor; Previous is disabled on page 0.
 * FR-011-4: force-delete via the confirm modal (DangerZone.tsx precedent — pf-modal-* classes reused).
 * Success = 204 NoContent OR 404 NOT_FOUND (EC-011-2: in-flight SAS may outlive the row — no client
 * special handling; render success immediately, then refetch). Operator role: both row action buttons
 * are disabled with a tooltip — NOT hidden (SuperAdmin-only force-delete via the T-011-01 gate).
 */
export function AdminTransfersPage({ role }: AdminTransfersPageProps) {
  // Filter bar inputs (uncontrolled until Search is pressed — the form owns the search lifecycle).
  const [q, setQ] = useState('')
  const [status, setStatus] = useState<string>('all')
  const [minBytes, setMinBytes] = useState('')
  const [maxBytes, setMaxBytes] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  // The committed search — the form's submit writes it; pagination reuses it via the ref.
  const paramsRef = useRef<SearchParams>({})
  const cursorsRef = useRef<(string | null)[]>([null])   // cursorsRef[i] loads page i+1; page 0 is null.
  const pageIndexRef = useRef(0)                         // current page index — force-delete refetches THIS page, not page 0.

  const [rows, setRows] = useState<AdminTransferRowDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [pageIndex, setPageIndex] = useState(0)
  const [hasNext, setHasNext] = useState(false)
  const [refreshTick, setRefreshTick] = useState(0)      // force-delete success → refetch the current page.

  // Force-delete confirm modal state (DangerZone.tsx precedent).
  const [deleteTargetId, setDeleteTargetId] = useState<string | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  // Escape closes the modal (= cancel; nothing sent on close-after-failure — DangerZone precedent).
  useEffect(() => {
    if (deleteTargetId === null) return
    function onKey(e: KeyboardEvent): void { if (e.key === 'Escape') setDeleteTargetId(null) }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey) }
  }, [deleteTargetId])

  // Initial load + refetch after a successful force-delete (refreshTick). Targets the CURRENT page —
  // a SuperAdmin on page 2+ must not be bounced back to page 1 when one row disappears.
  useEffect(() => {
    void fetchPage(pageIndexRef.current)
  }, [refreshTick])

  async function fetchPage(index: number): Promise<void> {
    const cursor = cursorsRef.current[index]   // null for page 0, opaque for later pages.
    setLoading(true)
    setError(null)
    try {
      const qs = buildSearchQuery({ ...paramsRef.current, cursor: cursor ?? undefined })
      const res = await fetch(`${API_BASE}/admin/transfers?${qs}`, { credentials: 'include', headers: { traceparent: getTraceParent() } })
      if (!res.ok) throw new Error('Could not load transfers.')
      const data = parseSearchResponse((await res.json()) as { items?: unknown[]; nextCursor?: string | null })
      // TA-4.1.4: the returned nextCursor becomes the cursor for the NEXT page past this one.
      cursorsRef.current[index + 1] = data.nextCursor
      setRows(data.rows)
      setPageIndex(index)
      pageIndexRef.current = index   // keep the ref in sync so force-delete's refetch hits THIS page.
      setHasNext(data.nextCursor !== null)   // Next enabled only when the current page still has a next.
    } catch {
      setError('Could not load transfers.')
    } finally {
      setLoading(false)
    }
  }

  function onSearchSubmit(e: React.FormEvent<HTMLFormElement>): void {
    e.preventDefault()   // no navigation — the screen owns its own fetch lifecycle.
    const next: SearchParams = {}
    if (q.trim()) next.q = q
    if (status !== 'all') next.status = status
    // Pairing XOR is enforced server-side — only send a pair when BOTH are filled (else 400 VALIDATION).
    if (minBytes && maxBytes) {
      next.minBytes = Number(minBytes)
      next.maxBytes = Number(maxBytes)
    }
    if (from && to) {
      next.from = from
      next.to = to
    }
    paramsRef.current = next
    cursorsRef.current = [null]   // a new search resets paging (FilesPage pill-change precedent).
    void fetchPage(0)
  }

  const deleteTarget = rows.find((r) => r.id === deleteTargetId)

  /**
   * FR-011-4: DELETE /api/v1/admin/transfers/{id}. Success = 204 NoContent OR 404 with a problem body
   * of code === 'NOT_FOUND' (EC-011-2: already force-deleted in another tab — no error, render success).
   * Success → role=status toast + refetch the current page (the row is gone server-side). Failure
   * (anything else) → inline role=alert error in the modal; the modal stays open.
   */
  async function confirmDelete(): Promise<void> {
    const id = deleteTargetId
    if (id === null) return
    setDeleting(true)
    setDeleteError(null)
    try {
      const res = await fetch(`${API_BASE}/admin/transfers/${id}`, { method: 'DELETE', credentials: 'include', headers: { traceparent: getTraceParent() } })
      const parsedBody = (await res.json().catch(() => null)) as { code?: string } | null
      const ok = res.status === 204 || (res.status === 404 && parsedBody?.code === 'NOT_FOUND')
      if (!ok) throw new Error('Could not force-delete the transfer. Please try again.')
      setDeleteTargetId(null)
      setToast('Transfer force-deleted.')
      setRefreshTick((t) => t + 1)   // refetch the current page — the row is gone server-side.
    } catch {
      setDeleteError('Could not force-delete the transfer. Please try again.')   // role=alert; modal stays open.
    } finally {
      setDeleting(false)   // only reachable when the modal is still mounted (the error path).
    }
  }

  function openDelete(row: AdminTransferRowDto): void {
    setDeleteError(null)   // a fresh open must not show the previous failure.
    setDeleteTargetId(row.id)
  }

  function closeDelete(): void {
    setDeleteTargetId(null)
    setDeleteError(null)
  }

  // D4 (FilesPage precedent): View opens the recipient page in a new context — /t/{linkId} is a guest route.
  function viewLink(row: AdminTransferRowDto): void {
    window.open(`${window.location.origin}/t/${row.linkId}`, '_blank', 'noopener')
  }

  const prevDisabled = pageIndex === 0

  return (
    <div className="ad-transfers">
      {/* FR-011-3: the five search dimensions — one q box (server OR), status, size pair, date pair. */}
      <form className="ad-filter-bar" onSubmit={onSearchSubmit}>
        <input
          type="text"
          className="ad-filter-input"
          placeholder="Link ID or recipient email"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        <select className="ad-filter-select" value={status} onChange={(e) => setStatus(e.target.value)}>
          {STATUS_OPTIONS.map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </select>
        <input type="number" className="ad-filter-input" placeholder="Min bytes" value={minBytes} onChange={(e) => setMinBytes(e.target.value)} />
        <input type="number" className="ad-filter-input" placeholder="Max bytes" value={maxBytes} onChange={(e) => setMaxBytes(e.target.value)} />
        <input type="date" className="ad-filter-input" aria-label="From date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <input type="date" className="ad-filter-input" aria-label="To date" value={to} onChange={(e) => setTo(e.target.value)} />
        <button type="submit" className="au-ghost-btn">Search</button>
      </form>

      {loading && <p className="ad-loading">Loading transfers…</p>}

      {!loading && error && (
        <p role="alert" className="mf-error">
          {error}
          {' '}
          <button type="button" className="mf-retry" onClick={() => void fetchPage(pageIndex)}>Try again</button>
        </p>
      )}

      {!loading && !error && rows.length > 0 && (
        <>
          {/* §4.7 table — ad- classes; status via overviewData.ts maps (reused, not duplicated). */}
          <table className="ad-table">
            <thead>
              <tr>
                <th>Link ID</th>
                <th>Owner email</th>
                <th>Status</th>
                <th>Files</th>
                <th>Size</th>
                <th>Created</th>
                <th>Downloads</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td className="ad-linkid">{row.linkId}</td>
                  <td>{row.ownerEmail ?? '—'}</td>
                  <td><span className={`ad-chip ad-chip-${statusTone(row.status)}`}>{statusLabel(row.status)}</span></td>
                  <td>{row.fileCount}</td>
                  <td>{formatBytes(row.totalBytes)}</td>
                  <td>{formatCreatedAt(row.createdAtUtc)}</td>
                  <td>{row.downloadsCount}</td>
                  {/* Operator: disabled with a tooltip — NOT hidden (exit check). */}
                  <td className="ad-row-actions">
                    <button
                      type="button"
                      className="au-ghost-btn ad-row-action"
                      aria-label="View"
                      title={role === 'Operator' ? 'SuperAdmin only' : undefined}
                      disabled={role === 'Operator'}
                      onClick={() => viewLink(row)}
                    >
                      View
                    </button>
                    <button
                      type="button"
                      className="au-ghost-btn ad-danger ad-row-action"
                      aria-label="Force delete"
                      title={role === 'Operator' ? 'SuperAdmin only' : undefined}
                      disabled={role === 'Operator'}
                      onClick={() => openDelete(row)}
                    >
                      Force-delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="ad-pagination">
            <button type="button" className="mf-prev" disabled={prevDisabled} onClick={() => void fetchPage(pageIndex - 1)}>
              Previous
            </button>
            <button type="button" className="mf-next" disabled={!hasNext} onClick={() => void fetchPage(pageIndex + 1)}>
              Next
            </button>
          </div>
        </>
      )}

      {!loading && !error && rows.length === 0 && (
        <p className="ad-loading">No transfers found.</p>
      )}

      {/* FR-011-4: force-delete confirm modal — DangerZone.tsx structure mirrored exactly. */}
      {deleteTarget !== undefined && (
        <div className="pf-modal-backdrop" onClick={closeDelete} aria-hidden="true">
          <div className="pf-modal" role="dialog" aria-modal="true" aria-labelledby="ad-delete-title" onClick={(e) => e.stopPropagation()}>
            <h2 className="pf-modal-title" id="ad-delete-title">Force-delete transfer?</h2>
            {/* Pinned copy (F-TRF-011-admin.md L54): "Force-delete transfer {linkId}? {fileCount} files, {formatBytes(totalBytes)}." */}
            <p className="pf-modal-copy">
              Force-delete transfer {deleteTarget.linkId}? {deleteTarget.fileCount === 1 ? '1 file' : `${deleteTarget.fileCount} files`}, {formatBytes(deleteTarget.totalBytes)}.
            </p>
            {deleteError && <p role="alert" className="pf-modal-error">{deleteError}</p>}
            <div className="pf-modal-actions">
              <button type="button" className="pf-modal-cancel" disabled={deleting} onClick={closeDelete}>Cancel</button>
              <button type="button" className="pf-modal-confirm" autoFocus disabled={deleting} onClick={() => void confirmDelete()} aria-busy={deleting}>
                {deleting ? 'Force-deleting…' : 'Force-delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Success state (EC-011-2): renders immediately after 204 — in-flight SAS may outlive the row. */}
      {toast !== null && <p role="status" className="dz-toast">{toast}</p>}
    </div>
  )
}

export default AdminTransfersPage
