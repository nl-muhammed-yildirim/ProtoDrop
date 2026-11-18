import { useEffect, useState } from 'react'

import type { AuditRecentRowDto, FlagRowDto } from './adminData'
import { formatCreatedAt } from './adminData'
import { getTraceParent } from '../../core/traceparent'

interface AdminFlagsPageProps {
  role: 'SuperAdmin' | 'Operator'   // T-011-10 precedent — AdminShell narrows /auth/me's role and passes it down.
}

// T-011-03 (FR-011-2): fixed local port, no proxy, no env vars yet (AdminShell + FilesPage precedent).
const API_BASE = 'http://localhost:8080/api/v1'

// US-044-01 (AC-044-3 / EC-044-1): inline copy for a 400 FLAG_INVALID.
const INVALID_COPY = 'Invalid value (400 FLAG_INVALID) — the previous value stays live.'

// Plan segment / field part of a `limits.<plan>.<field>` key.
function planOf(key: string): string {
  return key.split('.')[1] ?? ''
}
function fieldOf(key: string): string {
  return key.split('.').slice(2).join('.')
}

/**
 * T-011-13 / US-044-01 (FR-011-2 / FR-011-5 / FR-044-5): admin Flags screen at /admin/flags — endpoints 23 + 25.
 * Limits group: one table per plan for the integer limit rows (type 'int') — effective value (text input,
 * per-key Save), the plan default and an "(overridden)" marker when the value differs from it.
 * US-044-02 (FR-044-7): Features group — bool rows whose key starts "feature." render as a switch (title = the
 * seeded description) with the same per-key Save; toggling sets the draft to 'true'/'false'.
 * Every other row stays in the generic key/value editor. Save PUTs the draft as the RAW body string
 * (NOT JSON.stringify'd — the server validates it). res.ok (200 {key,value}) → refetch BOTH /admin/flags and
 * /admin/audit-recent (AC-011-2). 400 FLAG_INVALID → inline per-row role=alert .mf-error; any other failure
 * → inline per-row role=alert; in both cases the old value stays live (EC-044-1 — no optimistic update).
 * Operator: every Save button disabled + title "SuperAdmin only" — NOT hidden.
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
  const [saveError, setSaveError] = useState<{ key: string; message: string } | null>(null)

  useEffect(() => {
    void loadAll()
  }, [])

  async function loadAll(): Promise<void> {
    setLoading(true)
    setError(null)
    try {
      const [flagsRes, auditRes] = await Promise.all([
        fetch(`${API_BASE}/admin/flags`, { credentials: 'include', headers: { traceparent: getTraceParent() } }),
        fetch(`${API_BASE}/admin/audit-recent`, { credentials: 'include', headers: { traceparent: getTraceParent() } }),
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
   * FR-011-2 / FR-044-5: PUT /api/v1/admin/flags/{key} — the RAW BODY STRING is the new value (verbatim, NOT
   * JSON.stringify'd). Success = res.ok (200 {key,value}) → refetch BOTH lists (AC-011-2). 400 FLAG_INVALID →
   * inline per-row copy (EC-044-1); any other failure → inline role=alert; old value stays live.
   */
  async function saveKey(key: string): Promise<void> {
    setSavingKey(key)
    setSaveError(null)
    try {
      const res = await fetch(`${API_BASE}/admin/flags/${key}`, {
        method: 'PUT',
        credentials: 'include',
        headers: { traceparent: getTraceParent() },
        body: drafts[key] ?? '',   // the raw string, verbatim — the server reads it as the value.
      })
      if (res.status === 400) {
        setSaveError({ key, message: INVALID_COPY })   // EC-044-1 — the old value stays live.
        setDrafts((d) => {
          const rest = { ...d }   // drop the rejected draft so the field shows the live value again.
          delete rest[key]
          return rest
        })
        return
      }
      if (!res.ok) throw new Error('Could not save the flag. Please try again.')
      await loadAll()   // AC-011-2: refetch BOTH /admin/flags and /admin/audit-recent.
    } catch {
      setSaveError({ key, message: 'Could not save the flag. Please try again.' })   // role=alert; old value stays live.
    } finally {
      setSavingKey(null)
    }
  }

  const operator = role === 'Operator'

  // US-044-01: integer limit rows grouped by plan (key order kept); everything else stays in the generic table.
  const limitRows = rows.filter((r) => r.type === 'int')
  // US-044-02: phase-gate switches (bool + "feature." prefix) get their own group.
  const featureRows = rows.filter((r) => r.type === 'bool' && r.key.startsWith('feature.'))
  const otherRows = rows.filter((r) => r.type !== 'int' && !featureRows.includes(r))
  const limitPlans: string[] = []
  for (const r of limitRows) {
    const plan = planOf(r.key)
    if (!limitPlans.includes(plan)) limitPlans.push(plan)
  }

  function rowError(key: string) {
    return saveError !== null && saveError.key === key
      ? <p role="alert" className="mf-error">{saveError.message}</p>
      : null
  }

  function saveButton(key: string) {
    // Operator: disabled with a tooltip — NOT hidden (AC-011-3 UI half).
    return (
      <button
        type="button"
        className="au-ghost-btn ad-row-action"
        title={operator ? 'SuperAdmin only' : undefined}
        disabled={operator || savingKey === key}
        onClick={() => void saveKey(key)}
      >
        Save
      </button>
    )
  }

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
          {/* US-044-01 (FR-044-5): Limits group — one table per plan, effective value + plan default + overridden marker. */}
          {limitPlans.length > 0 && <h2 className="mf-h1">Limits</h2>}
          {limitPlans.map((plan) => (
            <table className="ad-table" key={plan} data-plan={plan}>
              <caption>{plan}</caption>
              <thead>
                <tr>
                  <th>Limit</th>
                  <th>Value</th>
                  <th>Plan default</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {limitRows.filter((r) => planOf(r.key) === plan).map((row) => (
                  <tr key={row.key}>
                    <td title={row.key}>{fieldOf(row.key)}</td>
                    <td>
                      <input
                        type="text"
                        className="ad-filter-input"
                        aria-label={row.key}
                        value={drafts[row.key] ?? row.value}
                        onChange={(e) => setDrafts((d) => ({ ...d, [row.key]: e.target.value }))}
                      />
                      {row.overridden && <span className="ad-hint"> (overridden)</span>}
                      {rowError(row.key)}
                    </td>
                    <td>{row.planDefault ?? ''}</td>
                    <td className="ad-row-actions">{saveButton(row.key)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ))}

          {/* US-044-02 (FR-044-7): Features group — one switch per phase gate, description as tooltip. */}
          {featureRows.length > 0 && (
            <>
              <h2 className="mf-h1">Features</h2>
              <table className="ad-table" data-group="features">
                <thead>
                  <tr>
                    <th>Feature</th>
                    <th>Enabled</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {featureRows.map((row) => (
                    <tr key={row.key}>
                      <td title={row.description ?? undefined}>{row.key}</td>
                      <td>
                        <input
                          type="checkbox"
                          role="switch"
                          aria-label={row.key}
                          title={row.description ?? undefined}
                          checked={(drafts[row.key] ?? row.value) === 'true'}
                          disabled={operator}
                          onChange={(e) => setDrafts((d) => ({ ...d, [row.key]: e.target.checked ? 'true' : 'false' }))}
                        />
                        {rowError(row.key)}
                      </td>
                      <td className="ad-row-actions">{saveButton(row.key)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

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
              {otherRows.map((row) => (
                <tr key={row.key}>
                  <td>{row.key}</td>
                  <td>
                    <textarea
                      className="ad-filter-input"
                      rows={2}
                      value={drafts[row.key] ?? row.value}
                      onChange={(e) => setDrafts((d) => ({ ...d, [row.key]: e.target.value }))}
                    />
                    {rowError(row.key)}
                  </td>
                  <td><span className="ad-json-preview">{row.value}</span></td>
                  <td className="ad-row-actions">{saveButton(row.key)}</td>
                </tr>
              ))}
            </tbody>
          </table>

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
