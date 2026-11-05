import { useEffect, useState } from 'react'

import type { AuditRecentRowDto, FlagRowDto } from './adminData'
import { formatCreatedAt } from './adminData'

interface AdminFlagsPageProps {
  role: 'SuperAdmin' | 'Operator'   // T-011-10 precedent — AdminShell narrows /auth/me's role and passes it down.
}

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AdminShell + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

/**
 * T-011-13 (FR-011-2 / FR-011-5): admin Flags screen at /admin/flags — endpoints 23 + 25.
 * Key/value editor: the textarea holds the RAW string (the wire value verbatim); Save PUTs it as the
 * raw body string (NOT JSON.stringify'd — the server reads it as the value and validates). 204 →
 * refetch BOTH /admin/flags and /admin/audit-recent (the Recent actions panel must show the new audit
 * row — AC-011-2). 400 VALIDATION → inline "Invalid JSON" via role=alert .mf-error; any other failure
 * → inline role=alert; in both cases the old value stays live in the textarea (EC-011-4 — no optimistic
 * update, no local parse). Operator: every Save button disabled + title "SuperAdmin only" — NOT hidden.
 * Recent actions panel (a second .ad-table): last 50 rows newest-first via GET /admin/audit-recent.
 */
export function AdminFlagsPage({ role }: AdminFlagsPageProps) {
  const [rows, setRows] = useState<FlagRowDto[]>([])
  const [auditRows, setAuditRows] = useState<AuditRecentRowDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // Per-key editable raw string (initial value = the wire value). The PUT body is this string verbatim.
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [savingKey, setSavingKey] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)

  useEffect(() => {
    void loadAll()
  }, [])

  async function loadAll(): Promise<void> {
    setLoading(true)
    setError(null)
    try {
      const [flagsRes, auditRes] = await Promise.all([
        fetch(`${API_BASE}/admin/flags`, { credentials: 'include' }),
        fetch(`${API_BASE}/admin/audit-recent`, { credentials: 'include' }),
      ])
      if (!flagsRes.ok || !auditRes.ok) throw new Error('Could not load flags.')
      const flags = (await flagsRes.json()) as FlagRowDto[]
      const audit = (await auditRes.json()) as AuditRecentRowDto[]
      setRows(flags)
      setAuditRows(audit)
      setDrafts(Object.fromEntries(flags.map((f) => [f.key, f.value])))   // drafts mirror the wire values.
    } catch {
      setError('Could not load flags.')
    } finally {
      setLoading(false)
    }
  }

  /**
   * FR-011-2: PUT /api/v1/admin/flags/{key} — the RAW BODY STRING is the new value (verbatim, NOT
   * JSON.stringify'd). Success = 204 No Content → refetch BOTH lists (AC-011-2). 400 VALIDATION →
   * inline "Invalid JSON" (EC-011-4); any other failure → inline role=alert; old value stays live.
   */
  async function saveKey(key: string): Promise<void> {
    setSavingKey(key)
    setSaveError(null)
    try {
      const res = await fetch(`${API_BASE}/admin/flags/${key}`, {
        method: 'PUT',
        credentials: 'include',
        body: drafts[key] ?? '',   // the raw string, verbatim — the server reads it as the value.
      })
      if (res.status === 400) {
        setSaveError('Invalid JSON')   // EC-011-4 — the old value stays live in the textarea.
        return
      }
      if (res.status !== 204) throw new Error('Could not save the flag. Please try again.')
      await loadAll()   // AC-011-2: refetch BOTH /admin/flags and /admin/audit-recent.
    } catch {
      setSaveError('Could not save the flag. Please try again.')   // role=alert; old value stays live.
    } finally {
      setSavingKey(null)
    }
  }

  const operator = role === 'Operator'

  return (
    <div className="ad-flags">
      {loading && <p className="ad-loading">Loading flags…</p>}

      {!loading && error && (
        <p role="alert" className="mf-error">
          {error}
          {' '}
          <button type="button" className="mf-retry" onClick={() => void loadAll()}>Try again</button>
        </p>
      )}

      {!loading && !error && rows.length > 0 && (
        <>
          {/* FR-011-2: key/value editor — the textarea holds the raw string; the preview shows it read-only. */}
          <table className="ad-table">
            <thead>
              <tr>
                <th>Key</th>
                <th>Value</th>
                <th>Preview</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.key}>
                  <td>{row.key}</td>
                  <td>
                    <textarea
                      className="ad-filter-input"
                      rows={2}
                      value={drafts[row.key] ?? row.value}
                      onChange={(e) => setDrafts((d) => ({ ...d, [row.key]: e.target.value }))}
                    />
                  </td>
                  <td><span className="ad-json-preview">{row.value}</span></td>
                  <td className="ad-row-actions">
                    {/* Operator: disabled with a tooltip — NOT hidden (AC-011-3 UI half). */}
                    <button
                      type="button"
                      className="au-ghost-btn ad-row-action"
                      title={operator ? 'SuperAdmin only' : undefined}
                      disabled={operator || savingKey === row.key}
                      onClick={() => void saveKey(row.key)}
                    >
                      Save
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {saveError !== null && <p role="alert" className="mf-error">{saveError}</p>}

          {/* Pinned copy (F-TRF-011-admin.md) — verbatim. */}
          <p className="ad-hint">Changes take effect within 30 seconds.</p>
          <p className="ad-hint">Changes are logged.</p>

          {/* FR-011-5: Recent actions panel — last 50 rows, newest first (endpoint 25). */}
          <h2 className="mf-h1">Recent actions</h2>
          {auditRows.length > 0 && (
            <table className="ad-table">
              <thead>
                <tr>
                  <th>Actor</th>
                  <th>Action</th>
                  <th>Target</th>
                  <th>Time</th>
                </tr>
              </thead>
              <tbody>
                {auditRows.map((row) => (
                  <tr key={row.id}>
                    <td>{row.actorEmail}</td>
                    <td>{row.action}</td>
                    <td>{row.entityType} {row.entityId}</td>
                    {/* --fs-small via .ad-table tbody td (index.css L1374–L1379). */}
                    <td>{formatCreatedAt(row.createdAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </div>
  )
}

export default AdminFlagsPage
