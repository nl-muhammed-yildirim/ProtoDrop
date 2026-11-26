import { useEffect, useRef, useState } from 'react'

import { API_BASE } from '../../core/config/apiBase'
import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import type { ErasureRowDto } from './erasuresData'
import { buildErasuresQuery, isLagging, parseErasuresResponse, toCsv } from './erasuresData'
import { formatCreatedAt } from './usersData'

// Fixed local port, no proxy, base from VITE_WA_API_BASE (CHORE-deploy-hardening) (AdminUsersPage precedent).

/**
 * US-047-03 (FR-047-6, UI-Reference §5.6): read-only Admin → Erasures screen — GET /admin/erasures.
 * Masking is decided server-side (SuperAdmin = DPO sees full emails, Operator sees d****@x.com), so this
 * page takes no role prop. Export CSV walks every page (not only the visible one) and downloads client-side.
 * No toast on errors: the screen is the alarm (role=alert inline, Users precedent).
 */
export function AdminErasuresPage() {
  const cursorsRef = useRef<(string | null)[]>([null]) // cursorsRef[i] loads page i; page 0 is null.
  const [rows, setRows] = useState<ErasureRowDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [pageIndex, setPageIndex] = useState(0)
  const [hasNext, setHasNext] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  useEffect(() => {
    void fetchPage(0)
  }, [])

  async function fetchRaw(cursor: string | null): Promise<{ rows: ErasureRowDto[]; nextCursor: string | null }> {
    const res = await fetch(`${API_BASE}/admin/erasures?${buildErasuresQuery(cursor)}`, {
      credentials: 'include',
      headers: { traceparent: getTraceParent() },
    })
    if (!res.ok) throw new Error('request failed')
    return parseErasuresResponse((await res.json()) as { items?: unknown; nextCursor?: string | null })
  }

  async function fetchPage(index: number): Promise<void> {
    setLoading(true)
    setError(null)
    try {
      const data = await fetchRaw(cursorsRef.current[index] ?? null)
      cursorsRef.current[index + 1] = data.nextCursor
      setRows(data.rows)
      setPageIndex(index)
      setHasNext(data.nextCursor !== null)
    } catch {
      setError(i18n.t('admin.erasures.loadError'))
    } finally {
      setLoading(false)
    }
  }

  async function exportCsv(): Promise<void> {
    setExporting(true)
    setExportError(null)
    try {
      const all: ErasureRowDto[] = []
      let cursor: string | null = null
      do {
        const data = await fetchRaw(cursor)
        all.push(...data.rows)
        cursor = data.nextCursor
      } while (cursor !== null)

      const blob = new Blob([toCsv(all)], { type: 'text/csv;charset=utf-8' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `erasures-${new Date().toISOString().slice(0, 10)}.csv`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch {
      setExportError(i18n.t('admin.erasures.exportError'))
    } finally {
      setExporting(false)
    }
  }

  const nowMs = Date.now()

  return (
    <div className="ad-erasures">
      <h1 className="mf-h1">{i18n.t('admin.erasures.title')}</h1>
      <p className="ad-loading">{i18n.t('admin.erasures.helper')}</p>
      <div className="ad-filter-bar">
        <button type="button" className="au-ghost-btn" disabled={exporting} onClick={() => void exportCsv()} aria-busy={exporting}>
          {exporting ? i18n.t('admin.erasures.exporting') : i18n.t('admin.erasures.exportCsv')}
        </button>
      </div>

      {exportError && <p role="alert" className="mf-error">{exportError}</p>}

      {loading && <p className="ad-loading">{i18n.t('admin.erasures.loading')}</p>}

      {!loading && error && (
        <p role="alert" className="mf-error">
          {error}
          {' '}
          <button type="button" className="mf-retry" onClick={() => void fetchPage(pageIndex)}>{i18n.t('admin.erasures.retry')}</button>
        </p>
      )}

      {!loading && !error && rows.length > 0 && (
        <>
          <table className="ad-table">
            <thead>
              <tr>
                <th>{i18n.t('admin.erasures.colEmail')}</th>
                <th>{i18n.t('admin.erasures.colDeletedAt')}</th>
                <th>{i18n.t('admin.erasures.colReHomed')}</th>
                <th>{i18n.t('admin.erasures.colSweep')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const lagging = isLagging(row, nowMs)
                return (
                  <tr
                    key={row.userId}
                    className={lagging ? 'ad-row-lagging' : undefined}
                    title={lagging ? i18n.t('admin.erasures.lagging') : undefined}
                  >
                    <td>{row.email ?? '—'}</td>
                    <td>{formatCreatedAt(row.deletedAtUtc)}</td>
                    <td>{row.reHomedTransferCount}</td>
                    <td>
                      {row.sweepStatus === 'swept' && row.sweptAtUtc
                        ? i18n.t('admin.erasures.swept', { time: formatCreatedAt(row.sweptAtUtc) })
                        : i18n.t('admin.erasures.pending')}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          <div className="ad-pagination">
            <button type="button" className="mf-prev" disabled={pageIndex === 0} onClick={() => void fetchPage(pageIndex - 1)}>
              {i18n.t('admin.erasures.prev')}
            </button>
            <button type="button" className="mf-next" disabled={!hasNext} onClick={() => void fetchPage(pageIndex + 1)}>
              {i18n.t('admin.erasures.next')}
            </button>
          </div>
        </>
      )}

      {!loading && !error && rows.length === 0 && (
        <p className="ad-loading">{i18n.t('admin.erasures.empty')}</p>
      )}
    </div>
  )
}

export default AdminErasuresPage
