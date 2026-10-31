import { useEffect, useRef, useState } from 'react'

import { formatBytes } from '../../core/upload/formatBytes'
import type { ListTransfersResponse, MyTransferRowDto } from './formatting'
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

// T-023 (F-TRF-010): the re-send endpoint POST /api/v1/transfers/{id}/resend ships there — route stub only for now.
function ReSendStubToast(): string {
  return 'Re-send lands in T-023 (F-TRF-010).'
}

/**
 * T-009-05 (UI-Reference §5.4): My Files screen at /files (signed-in only — App guards the branch).
 * Cursor-paged transfer history from endpoint 14 (GET /api/v1/transfers?status=&cursor=, TA-4.2#14),
 * fixed server sort CreatedAtUtc DESC (TA-4.1.4). Filter pills All/Active/Expired forward the pill's
 * wire value to ?status= — the client NEVER re-filters rows (US-009-02 canonical mapping is server-side).
 * One 1s setInterval tick (AuthPage cooldown-interval precedent) drives every row's countdown — there
 * is exactly ONE interval for the whole list and no other timer/polling anywhere.
 * T-009-06 (FR-009-3, AC-009-2 client half): per-row ghost actions (Copy link / View / Re-send / Delete)
 * + delete confirm modal (DangerZone.tsx structure mirrored — pf-modal-* classes reused). Success = 204
 * NoContent OR 404 with problem body code === 'NOT_FOUND' (EC-009-2); the row is removed locally and a
 * role=status toast "Transfer deleted." auto-dismisses in 5 s. Nothing is sent until Confirm — Cancel/
 * Escape close only.
 */
export function FilesPage({ onNewTransfer }: FilesPageProps) {
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'expired'>('all')
  const [rows, setRows] = useState<ListTransfersResponse['items']>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [nowMs, setNowMs] = useState(() => Date.now())

  // T-009-06: delete confirm modal — files-page-owned (the row list is what it guards).
  const [deleteTargetId, setDeleteTargetId] = useState<string | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  // T-009-06 (D-6): files-page-owned success toast (NOT App's deleteToast — that is DropZone-scoped).
  // The 5 s timer auto-dismisses; the timeout id is cleared on unmount.
  const [toast, setToast] = useState<string | null>(null)
  const toastTimerRef = useRef<number | null>(null)

  useEffect(() => {
    return () => {
      if (toastTimerRef.current !== null) window.clearTimeout(toastTimerRef.current)
    }
  }, [])

  // Escape closes the modal (= cancel; nothing sent on close-after-failure — DangerZone precedent).
  useEffect(() => {
    if (deleteTargetId === null) return
    function onKey(e: KeyboardEvent): void { if (e.key === 'Escape') setDeleteTargetId(null) }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey) }
  }, [deleteTargetId])

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

  function openDelete(row: MyTransferRowDto): void {
    setDeleteError(null)   // a fresh open must not show the previous failure
    setDeleteTargetId(row.id)
  }

  function closeDelete(): void {
    setDeleteTargetId(null)
    setDeleteError(null)
  }

  /**
   * T-009-06 (AC-009-2 client half): DELETE /api/v1/transfers/{id}. Success = 204 NoContent OR 404 with a
   * problem body of code === 'NOT_FOUND' (EC-009-2: a second tab deleting the same transfer — no error,
   * the row quietly vanishes). The body is parsed defensively (.catch(() => null)): a bare 404 WITHOUT a
   * code is NOT success. Success → remove row locally + role=status toast; failure (anything else) →
   * inline role=alert error in the modal, modal stays open.
   */
  async function confirmDelete(): Promise<void> {
    const id = deleteTargetId
    if (id === null) return
    setDeleting(true)
    setDeleteError(null)
    try {
      const res = await fetch(`${API_BASE}/transfers/${id}`, { method: 'DELETE', credentials: 'include' })
      const parsedBody = (await res.json().catch(() => null)) as { code?: string } | null
      const ok = res.status === 204 || (res.status === 404 && parsedBody?.code === 'NOT_FOUND')
      if (!ok) throw new Error('Could not delete the transfer. Please try again.')
      // EC-009-2: remove the row locally (it's gone server-side — a NOT_FOUND on re-fetch is expected).
      setRows((prev) => prev.filter((r) => r.id !== id))
      setDeleteTargetId(null)
      setToast('Transfer deleted.')   // role=status below — D-6 files-page-owned region.
      if (toastTimerRef.current !== null) window.clearTimeout(toastTimerRef.current)
      toastTimerRef.current = window.setTimeout(() => setToast(null), 5000)   // 5 s auto-dismiss (App deleteToast precedent)
    } catch {
      setDeleteError('Could not delete the transfer. Please try again.')   // role=alert; modal stays open.
    } finally {
      setDeleting(false)   // only reachable when the modal is still mounted (the error path).
    }
  }

  /**
   * T-009-06 (FR-009-3): copy `{origin}/t/{linkId}` — the recipient URL form pinned by LinkId.cs.
   * navigator.clipboard.writeText with the execCommand fallback pattern from LinkScreen.tsx:68.
   */
  async function copyLink(row: MyTransferRowDto): Promise<void> {
    const url = `${window.location.origin}/t/${row.linkId}`
    let ok = false
    try {
      await navigator.clipboard.writeText(url)
      ok = true
    } catch {
      // Fallback: temporary textarea + execCommand (non-secure contexts).
      const ta = document.createElement('textarea')
      ta.value = url
      ta.style.position = 'fixed'
      ta.style.opacity = '0'
      document.body.appendChild(ta)
      ta.select()
      try {
        ok = document.execCommand('copy')
      } finally {
        document.body.removeChild(ta)
      }
    }
    if (ok) {
      setToast(`Link copied: ${url}`)   // role=status — same 5 s auto-dismiss region.
      if (toastTimerRef.current !== null) window.clearTimeout(toastTimerRef.current)
      toastTimerRef.current = window.setTimeout(() => setToast(null), 5000)
    }
  }

  // T-009-06 (D4): View opens the recipient page in a new context — RecipientPage is a guest route (App.tsx branch).
  function viewLink(row: MyTransferRowDto): void {
    const url = `${window.location.origin}/t/${row.linkId}`
    window.open(url, '_blank', 'noopener')   // noopener: the recipient page must not reach back to this window.
  }

  // T-023 (F-TRF-010): route stub — the endpoint lands there; nothing is sent today, only this status toast.
  function resendStub(): void {
    setToast(ReSendStubToast())
    if (toastTimerRef.current !== null) window.clearTimeout(toastTimerRef.current)
    toastTimerRef.current = window.setTimeout(() => setToast(null), 5000)
  }

  const deleteTarget = rows.find((r) => r.id === deleteTargetId)

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
                  {/* T-009-06 (UI-Reference §5.4): row actions — ghost buttons (au-ghost-btn reuse). */}
                  <div className="mf-actions">
                    <button type="button" className="au-ghost-btn" aria-label="Copy link" onClick={() => void copyLink(row)}>Copy</button>
                    <button type="button" className="au-ghost-btn" aria-label="View" onClick={() => viewLink(row)}>View</button>
                    <button type="button" className="au-ghost-btn" aria-label="Re-send" onClick={resendStub}>Re-send</button>
                    <button type="button" className="au-ghost-btn" aria-label="Delete" onClick={() => openDelete(row)}>Delete</button>
                  </div>
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

      {/* T-009-06 (AC-009-2 client half): delete confirm modal — DangerZone.tsx structure mirrored exactly. */}
      {deleteTarget !== undefined && (
        <div className="pf-modal-backdrop" onClick={closeDelete} aria-hidden="true">
          <div className="pf-modal" role="dialog" aria-modal="true" aria-labelledby="mf-delete-title" onClick={(e) => e.stopPropagation()}>
            <h2 className="pf-modal-title" id="mf-delete-title">Delete transfer?</h2>
            {/* AC-009-2: the copy names exactly what's going away — count + size from the ROW data (formatBytes). */}
            <p className="pf-modal-copy">
              {deleteTarget.fileCount === 1 ? '1 file' : `${deleteTarget.fileCount} files`}, {formatBytes(deleteTarget.totalBytes)}. This can't be undone.
            </p>
            {deleteError && <p role="alert" className="pf-modal-error">{deleteError}</p>}
            <div className="pf-modal-actions">
              <button type="button" className="pf-modal-cancel" disabled={deleting} onClick={closeDelete}>Cancel</button>
              <button type="button" className="pf-modal-confirm" autoFocus disabled={deleting} onClick={() => void confirmDelete()} aria-busy={deleting}>
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* T-009-06 (D-6): files-page-owned status region — success toasts auto-dismiss in 5 s (reuses .dz-toast). */}
      {toast !== null && <p role="status" className="dz-toast">{toast}</p>}
    </div>
  )
}

export default FilesPage
