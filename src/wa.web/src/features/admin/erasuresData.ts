/**
 * US-047-03 (FR-047-6): wire types + pure helpers for the admin Erasures screen (GET /api/v1/admin/erasures).
 * NO React here — unit-tested by erasuresData.test.ts. Every function is total (never throws).
 */

// Mirrors the server's ErasureReportPolicy.PageSize (the TA-4.1.4 pin; usersData DEFAULT_PAGE_SIZE precedent).
export const ERASURE_PAGE_SIZE = 25

// A pending row older than this is highlighted (US-047-03 happy path 4) — a UI threshold, not a limit.
export const ERASURE_LAG_MS = 2 * 24 * 60 * 60 * 1000

export interface ErasureRowDto {
  userId: string
  email: string | null          // masked by the server for Operator; null once the user row is hard-deleted
  deletedAtUtc: string          // ISO
  reHomedTransferCount: number
  sweepStatus: 'pending' | 'swept'
  sweptAtUtc: string | null     // ISO
}

export const CSV_HEADER = 'userId,email,deletedAtUtc,reHomedTransferCount,sweepStatus,sweptAtUtc'

export function buildErasuresQuery(cursor?: string | null): string {
  const sp = new URLSearchParams()
  if (cursor) sp.set('cursor', cursor)
  sp.set('limit', String(ERASURE_PAGE_SIZE))
  return sp.toString()
}

export function parseErasuresResponse(json: { items?: unknown; nextCursor?: string | null } | null | undefined): {
  rows: ErasureRowDto[]
  nextCursor: string | null
} {
  const rows = (Array.isArray(json?.items) ? json.items : []) as ErasureRowDto[]
  return { rows, nextCursor: json?.nextCursor ?? null }
}

/** A pending row whose deletion is more than ERASURE_LAG_MS old. Swept rows never lag. */
export function isLagging(row: Pick<ErasureRowDto, 'sweepStatus' | 'deletedAtUtc'>, nowMs: number): boolean {
  if (row.sweepStatus !== 'pending') return false
  const deletedMs = Date.parse(row.deletedAtUtc)
  if (Number.isNaN(deletedMs)) return false
  return nowMs - deletedMs > ERASURE_LAG_MS
}

// RFC 4180 quoting plus a formula-injection guard (cells starting with = + - @ get a leading apostrophe).
function csvCell(value: string | number | null): string {
  if (value === null) return ''
  let s = String(value)
  if (/^[=+\-@]/.test(s)) s = `'${s}`
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
}

export function toCsv(rows: ErasureRowDto[]): string {
  const lines = rows.map((r) =>
    [r.userId, r.email, r.deletedAtUtc, r.reHomedTransferCount, r.sweepStatus, r.sweptAtUtc].map(csvCell).join(','),
  )
  return [CSV_HEADER, ...lines].join('\r\n') + '\r\n'
}
