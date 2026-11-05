// T-011-13 (FR-011-2 / FR-011-5): shared wire types + pure helpers for the Flags and Suppressions
// screens (endpoints 23/24/25 — Program.cs L706–L748, L463–L494, L497–L503). No React here.

// Endpoint 23 GET /api/v1/admin/flags — bare JSON array, ordered by key ascending, 52 fixed rows,
// NO paging (Program.cs L708–L715). Reads write no audit row (AC-011-3).
export interface FlagRowDto {
  key: string
  value: string
}

// Endpoint 24 GET /api/v1/admin/suppressions — bare array (Program.cs L463–L469). id is a Guid
// 'n'-form string (no dashes); senderEmail may be null.
export interface SuppressionRowDto {
  id: string
  address: string
  senderEmail: string | null
  createdAtUtc: string
}

// Endpoint 25 GET /api/v1/admin/audit-recent — bare array, last 50 rows, newest first
// (Program.cs L497–L503). Both roles read.
export interface AuditRecentRowDto {
  id: number
  actorEmail: string
  action: string
  entityType: string
  entityId: string
  detailsJson: string | null
  createdAtUtc: string
}

/**
 * FR-011-5 time column — locale month-day-year + time via toLocaleDateString/toLocaleTimeString
 * (en-US) so the copy is deterministic in tests. Total: unparseable ISO → stable fallback, no throw
 * (same style as usersData.formatCreatedAt).
 */
export function formatCreatedAt(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return 'Unknown'
  const date = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
  const time = d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
  return `${date}, ${time}`
}
