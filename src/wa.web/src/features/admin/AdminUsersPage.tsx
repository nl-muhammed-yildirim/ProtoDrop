import { useEffect, useRef, useState } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import type { AdminUserRowDto, SearchParams } from './usersData'
import { PLANS, buildSearchQuery, formatCreatedAt, parseSearchResponse, planIdForName } from './usersData'

interface AdminUsersPageProps {
  role: 'SuperAdmin' | 'Operator'   // T-011-10 precedent — AdminShell narrows /auth/me's role and passes it down.
}

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AdminShell + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

/**
 * T-011-12 (FR-011-2): admin Users screen at /admin/users — endpoint 22.
 * Filter bar: ONE q box (email substring, server trims + lowercases and matches AppUser.Email CI).
 * Cursor pagination at 25 rows (TA-4.1.4): Next/Previous walk the opaque nextCursor; Previous is
 * disabled on page 0. §4.7 table: Email, Plan (<select>), Storage used, Active transfers, Created.
 * Plan change → PATCH /admin/users/{id}?planId={guid} (SuperAdmin-only via T-011-01 gate); the select
 * value derives from row data so it reverts automatically on failure (no optimistic update). Over-quota
 * downgrades are allowed in the UI — no client-side block (US-011-03 alternative flows). Force-delete →
 * DELETE /admin/users/{id} via the confirm modal (AdminTransfersPage precedent); success = 204 OR 404
 * NOT_FOUND (EC-009-2 idempotency — treat as success, no error). Operator role: every plan <select> AND
 * every trash button disabled with a tooltip — NOT hidden.
 */
export function AdminUsersPage({ role }: AdminUsersPageProps) {
  // Filter bar input (uncontrolled until Search is pressed — the form owns the search lifecycle).
  const [q, setQ] = useState('')

  // The committed search — the form's submit writes it; pagination reuses it via the ref.
  const paramsRef = useRef<SearchParams>({})
  const cursorsRef = useRef<(string | null)[]>([null])   // cursorsRef[i] loads page i+1; page 0 is null.
  const pageIndexRef = useRef(0)                         // current page index — refetches hit THIS page, not page 0.

  const [rows, setRows] = useState<AdminUserRowDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [pageIndex, setPageIndex] = useState(0)
  const [hasNext, setHasNext] = useState(false)
  const [refreshTick, setRefreshTick] = useState(0)      // plan-change / force-delete success → refetch the current page.

  // Plan-change error (inline role=alert above the table — the select reverts automatically).
  const [planError, setPlanError] = useState<string | null>(null)

  // Force-delete confirm modal state (AdminTransfersPage precedent).
  const [deleteTargetId, setDeleteTargetId] = useState<string | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  // Escape closes the modal (= cancel; nothing sent on close-after-failure — AdminTransfersPage precedent).
  useEffect(() => {
    if (deleteTargetId === null) return
    function onKey(e: KeyboardEvent): void { if (e.key === 'Escape') setDeleteTargetId(null) }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey) }
  }, [deleteTargetId])

  // Initial load + refetch after a successful plan-change / force-delete (refreshTick). Targets the
  // CURRENT page — a SuperAdmin on page 2+ must not be bounced back to page 1 when one row disappears.
  useEffect(() => {
    void fetchPage(pageIndexRef.current)
  }, [refreshTick])

  async function fetchPage(index: number): Promise<void> {
    const cursor = cursorsRef.current[index]   // null for page 0, opaque for later pages.
    setLoading(true)
    setError(null)
    try {
      const qs = buildSearchQuery({ ...paramsRef.current, cursor: cursor ?? undefined })
      const res = await fetch(`${API_BASE}/admin/users?${qs}`, { credentials: 'include' })
      if (!res.ok) throw new Error('Could not load users.')
      const data = parseSearchResponse((await res.json()) as { items?: unknown[]; nextCursor?: string | null })
      // TA-4.1.4: the returned nextCursor becomes the cursor for the NEXT page past this one.
      cursorsRef.current[index + 1] = data.nextCursor
      setRows(data.rows)
      setPageIndex(index)
      pageIndexRef.current = index   // keep the ref in sync so refetches hit THIS page.
      setHasNext(data.nextCursor !== null)   // Next enabled only when the current page still has a next.
    } catch {
      setError('Could not load users.')
    } finally {
      setLoading(false)
    }
  }

  function onSearchSubmit(e: React.FormEvent<HTMLFormElement>): void {
    e.preventDefault()   // no navigation — the screen owns its own fetch lifecycle.
    const next: SearchParams = {}
    if (q.trim()) next.q = q
    paramsRef.current = next
    cursorsRef.current = [null]   // a new search resets paging (FilesPage pill-change precedent).
    void fetchPage(0)
  }

  const deleteTarget = rows.find((r) => r.id === deleteTargetId)

  /**
   * FR-011-2: PATCH /api/v1/admin/users/{id}?planId={guid}. The planId comes from the QUERY STRING (not a
   * body). Success → 204 NoContent → refetch the current page (the select value derives from row data, so
   * it updates automatically). Any other status → inline role=alert above the table; the select reverts
   * automatically because its value derives from row data (no optimistic update). Over-quota downgrades
   * are allowed in the UI — no client-side block (US-011-03 alternative flows).
   */
  async function onPlanChange(row: AdminUserRowDto, planId: string): Promise<void> {
    setPlanError(null)
    try {
      const res = await fetch(`${API_BASE}/admin/users/${row.id}?planId=${planId}`, { method: 'PATCH', credentials: 'include' })
      if (res.status !== 204) throw new Error('Could not save the plan. Please try again.')
      setRefreshTick((t) => t + 1)   // refetch the current page — the planName is updated server-side.
    } catch {
      setPlanError('Could not save the plan. Please try again.')   // role=alert; the select reverts automatically.
    }
  }

  /**
   * FR-011-2: DELETE /api/v1/admin/users/{id}. Success = 204 NoContent OR 404 with a problem body of
   * code === 'NOT_FOUND' (EC-009-2 idempotency: unknown/already soft-deleted — no error, render success).
   * Success → role=status toast + refetch the current page. Failure (anything else) → inline role=alert
   * error in the modal; the modal stays open.
   */
  async function confirmDelete(): Promise<void> {
    const id = deleteTargetId
    if (id === null) return
    setDeleting(true)
    setDeleteError(null)
    try {
      const res = await fetch(`${API_BASE}/admin/users/${id}`, { method: 'DELETE', credentials: 'include' })
      const parsedBody = (await res.json().catch(() => null)) as { code?: string } | null
      const ok = res.status === 204 || (res.status === 404 && parsedBody?.code === 'NOT_FOUND')
      if (!ok) throw new Error('Could not force-delete the user. Please try again.')
      setDeleteTargetId(null)
      setToast('User force-deleted.')
      setRefreshTick((t) => t + 1)   // refetch the current page — the row is gone server-side.
    } catch {
      setDeleteError('Could not force-delete the user. Please try again.')   // role=alert; modal stays open.
    } finally {
      setDeleting(false)   // only reachable when the modal is still mounted (the error path).
    }
  }

  function openDelete(row: AdminUserRowDto): void {
    setDeleteError(null)   // a fresh open must not show the previous failure.
    setDeleteTargetId(row.id)
  }

  function closeDelete(): void {
    setDeleteTargetId(null)
    setDeleteError(null)
  }

  const prevDisabled = pageIndex === 0
  const operator = role === 'Operator'   // SuperAdmin-only controls: disabled with a tooltip — NOT hidden.

  return (
    <div className="ad-users">
      {/* FR-011-2: the search dimension — one q box (email substring). */}
      <form className="ad-filter-bar" onSubmit={onSearchSubmit}>
        <input
          type="text"
          className="ad-filter-input"
          placeholder="Email"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        <button type="submit" className="au-ghost-btn">Search</button>
      </form>

      {loading && <p className="ad-loading">Loading users…</p>}

      {!loading && error && (
        <p role="alert" className="mf-error">
          {error}
          {' '}
          <button type="button" className="mf-retry" onClick={() => void fetchPage(pageIndex)}>Try again</button>
        </p>
      )}

      {!loading && !error && rows.length > 0 && (
        <>
          {/* FR-011-2: plan-change error — inline role=alert above the table (the select reverts automatically). */}
          {planError && <p role="alert" className="mf-error">{planError}</p>}

          {/* §4.7 table — ad- classes; plan via a <select> populated from the TA-14.3 pinned PLANS constant. */}
          <table className="ad-table">
            <thead>
              <tr>
                <th>Email</th>
                <th>Plan</th>
                <th>Storage used</th>
                <th>Active transfers</th>
                <th>Created</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>{row.email}</td>
                  {/* The select value derives from row data — it reverts automatically on a failed PATCH. */}
                  <td>
                    <select
                      className="ad-filter-select ad-row-action"
                      value={planIdForName(row.planName) ?? ''}
                      disabled={operator}
                      title={operator ? 'SuperAdmin only' : undefined}
                      onChange={(e) => {
                        const planId = e.target.value
                        if (planId) void onPlanChange(row, planId)
                      }}
                    >
                      {/* null/unknown planName → a disabled "—" placeholder option (the wire allows null). */}
                      {planIdForName(row.planName) === null && <option value="" disabled>—</option>}
                      {PLANS.map((p) => (
                        <option key={p.planId} value={p.planId}>{p.name}</option>
                      ))}
                    </select>
                  </td>
                  <td>{formatBytes(row.storageBytesUsed)}</td>
                  <td>{row.activeTransferCount}</td>
                  <td>{formatCreatedAt(row.createdAtUtc)}</td>
                  {/* Operator: disabled with a tooltip — NOT hidden (exit check). */}
                  <td className="ad-row-actions">
                    <button
                      type="button"
                      className="au-ghost-btn ad-danger ad-row-action"
                      aria-label="Force delete"
                      title={operator ? 'SuperAdmin only' : undefined}
                      disabled={operator}
                      onClick={() => openDelete(row)}
                    >
                      {/* §6 icon set: trash — inline SVG, 1.5 px stroke, 24 px grid (TA-2.6: no icon fonts). */}
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                        <path d="M3 6h18" />
                        <path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" />
                        <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                        <path d="M10 11v6" />
                        <path d="M14 11v6" />
                      </svg>
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
        <p className="ad-loading">No users found.</p>
      )}

      {/* FR-011-2: force-delete confirm modal — AdminTransfersPage structure mirrored exactly. */}
      {deleteTarget !== undefined && (
        <div className="pf-modal-backdrop" onClick={closeDelete} aria-hidden="true">
          <div className="pf-modal" role="dialog" aria-modal="true" aria-labelledby="ad-users-delete-title" onClick={(e) => e.stopPropagation()}>
            <h2 className="pf-modal-title" id="ad-users-delete-title">Force-delete user?</h2>
            {/* Decision (plan): "Force-delete {email}? Their active transfers will be re-homed to anonymous." */}
            <p className="pf-modal-copy">
              Force-delete {deleteTarget.email}? Their active transfers will be re-homed to anonymous.
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

      {/* Success state (EC-009-2): renders immediately after 204 / 404 NOT_FOUND. */}
      {toast !== null && <p role="status" className="dz-toast">{toast}</p>}
    </div>
  )
}

export default AdminUsersPage
