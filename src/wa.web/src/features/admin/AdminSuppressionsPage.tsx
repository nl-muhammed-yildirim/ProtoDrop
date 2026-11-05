import { useEffect, useState } from 'react'

import type { SuppressionRowDto } from './adminData'
import { formatCreatedAt } from './adminData'

interface AdminSuppressionsPageProps {
  role: 'SuperAdmin' | 'Operator'   // T-011-10 precedent — AdminShell narrows /auth/me's role and passes it down.
}

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AdminShell + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

/**
 * T-011-13 (FR-011-2 / FR-011-5): admin Suppressions screen at /admin/suppressions — endpoint 24.
 * List (address, sender, date) + Remove ghost per row → DELETE /api/v1/admin/suppressions/{id}.
 * Success = 200 OK (NOT NoContent — UnsubscribeEndpointTests pins it) → role=status toast .dz-toast +
 * refetch the list (the row is gone server-side). Failure (anything else) → inline role=alert, row stays.
 * Operator: every Remove button disabled + title "SuperAdmin only" — NOT hidden (endpoint 24 DELETE is
 * superadminOnly:true).
 */
export function AdminSuppressionsPage({ role }: AdminSuppressionsPageProps) {
  const [rows, setRows] = useState<SuppressionRowDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    void loadRows()
  }, [])

  async function loadRows(): Promise<void> {
    setLoading(true)
    setError(null)
    try {
      const res = await fetch(`${API_BASE}/admin/suppressions`, { credentials: 'include' })
      if (!res.ok) throw new Error('Could not load suppressions.')
      setRows((await res.json()) as SuppressionRowDto[])
    } catch {
      setError('Could not load suppressions.')
    } finally {
      setLoading(false)
    }
  }

  /**
   * FR-011-5: DELETE /api/v1/admin/suppressions/{id}. Success = 200 OK (KEEP 200 — NOT NoContent).
   * Success → role=status toast + refetch the list. Failure (anything else) → inline role=alert, row stays.
   */
  async function removeRow(row: SuppressionRowDto): Promise<void> {
    try {
      const res = await fetch(`${API_BASE}/admin/suppressions/${row.id}`, {
        method: 'DELETE',
        credentials: 'include',
      })
      if (res.status !== 200) throw new Error('Could not remove the suppression. Please try again.')
      setToast('Suppression removed.')
      await loadRows()   // refetch — the row is gone server-side.
    } catch {
      setError('Could not remove the suppression. Please try again.')   // role=alert; row stays.
    }
  }

  const operator = role === 'Operator'

  return (
    <div className="ad-suppressions">
      {loading && <p className="ad-loading">Loading suppressions…</p>}

      {!loading && error && (
        <p role="alert" className="mf-error">
          {error}
          {' '}
          <button type="button" className="mf-retry" onClick={() => void loadRows()}>Try again</button>
        </p>
      )}

      {!loading && !error && rows.length > 0 && (
        <table className="ad-table">
          <thead>
            <tr>
              <th>Address</th>
              <th>Sender</th>
              <th>Date</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td>{row.address}</td>
                <td>{row.senderEmail ?? '—'}</td>
                <td>{formatCreatedAt(row.createdAtUtc)}</td>
                <td className="ad-row-actions">
                  {/* Operator: disabled with a tooltip — NOT hidden (AC-011-3 UI half). */}
                  <button
                    type="button"
                    className="au-ghost-btn ad-danger ad-row-action"
                    aria-label="Remove"
                    title={operator ? 'SuperAdmin only' : undefined}
                    disabled={operator}
                    onClick={() => void removeRow(row)}
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {!loading && !error && rows.length === 0 && (
        <p className="ad-loading">No suppressions found.</p>
      )}

      {/* Success state: renders immediately after 200 OK. */}
      {toast !== null && <p role="status" className="dz-toast">{toast}</p>}
    </div>
  )
}

export default AdminSuppressionsPage
