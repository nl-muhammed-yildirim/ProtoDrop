/**
 * T-011-12 (FR-011-2): endpoint 22 types + pure helpers for the admin Users screen.
 * NO React here — unit-tested by usersData.test.ts. Every function is total (never throws)
 * so a bad wire row can't crash the screen. Do NOT import across features — admin owns its own helpers.
 */

// T-011-06 wire shape (TA-4.2#22), verified against src/wa.api/Program.cs L578–L614:
// GET /api/v1/admin/users returns { items: [...], nextCursor } — mirror the row fields exactly.
export interface AdminUserRowDto {
  id: string                // Guid as a JSON string — PATCH/DELETE route target
  email: string             // AppUser.Email
  planName: string | null   // null when no plan row — render a disabled "—" placeholder option
  storageBytesUsed: number  // long
  activeTransferCount: number // int
  createdAtUtc: string      // ISO DateTime string
}

// TA-14.3 pinned seeded Plan set (src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs L28–L35):
// the client's ONLY source of planId GUIDs — there is no plans-listing endpoint exposed to the client.
// The <select> options are built from this fixed name→GUID map in SortOrder; the wire planName is
// rendered as the visible label and mapped back to its GUID for the PATCH.
export const PLANS = [
  { name: 'Free', planId: '11111111-0000-4000-8000-000000000001' },
  { name: 'Pro', planId: '11111111-0000-4000-8000-000000000002' },
  { name: 'Business', planId: '11111111-0000-4000-8000-000000000003' },
] as const

// Total: unknown/null → null (the select renders a disabled "—" placeholder option in that case).
export function planIdForName(name: string | null): string | null {
  if (name === null) return null
  const found = PLANS.find((p) => p.name === name)
  return found ? found.planId : null
}

// FR-011-2: the search dimensions — q (email substring, ONE param; server trims + lowercases and
// matches AppUser.Email substring CI) + cursor (opaque server cursor, TA-4.1.4).
export interface SearchParams {
  q?: string
  cursor?: string
}

// TA-4.1.4 pin: default page size 25 — the endpoint rejects limit <=0 or >25 with 400 VALIDATION.
export const DEFAULT_PAGE_SIZE = 25

/**
 * Build the query string for GET /api/v1/admin/users. Omit absent keys; always send limit=25
 * (TA-4.1.4 pin — explicit is deterministic). The opaque cursor is forwarded verbatim on page 2+.
 */
export function buildSearchQuery(params: SearchParams): string {
  const sp = new URLSearchParams()
  if (params.q) sp.set('q', params.q)
  if (params.cursor) sp.set('cursor', params.cursor)
  sp.set('limit', String(DEFAULT_PAGE_SIZE))
  return sp.toString()
}

/**
 * Parse the endpoint-22 GET response into rows + nextCursor. Total: a missing/empty nextCursor is
 * null (last page); rows default to [] so a malformed body can't crash the screen.
 */
export function parseSearchResponse(json: { items?: unknown[]; nextCursor?: string | null }): {
  rows: AdminUserRowDto[]
  nextCursor: string | null
} {
  const rows = (Array.isArray(json.items) ? json.items : []) as AdminUserRowDto[]
  return { rows, nextCursor: json.nextCursor ?? null }
}

/**
 * FR-011-2 created column — locale month-day-year + time via toLocaleDateString/toLocaleTimeString
 * (en-US) so the copy is deterministic in tests. Total: unparseable ISO → stable fallback, no throw
 * (same style as transfersData.formatCreatedAt).
 */
export function formatCreatedAt(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return 'Unknown'
  const date = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
  const time = d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
  return `${date}, ${time}`
}
