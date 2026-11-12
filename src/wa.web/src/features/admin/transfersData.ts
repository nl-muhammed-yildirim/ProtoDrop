/**
 * T-011-11 (FR-011-3): endpoint 21 types + pure helpers for the admin Transfers screen.
 * NO React here — unit-tested by transfersData.test.ts. Every function is total (never throws)
 * so a bad wire row can't crash the screen. Do NOT import across features — admin owns its own helpers.
 */

import { formatDate, formatTime } from '../../core/i18n/format'

// T-011-04 wire shape (TA-4.2#21), verified against src/wa.api/Program.cs L550–L563:
// GET /api/v1/admin/transfers returns { items: [...], nextCursor } — mirror the row fields exactly.
// id is the T-011-11 one-line projection addition (Program.cs) — Guid 'n'-form string, needed by
// the DELETE route /admin/transfers/{id:guid}.
export interface AdminTransferRowDto {
  id: string                // Guid 'n'-form (32 hex chars) — force-delete target
  linkId: string            // 8-char Crockford
  ownerEmail: string | null // nullable — null → "—" in the table
  status: number            // byte 0..4 as a JSON number (src/wa.domain/Transfer.cs L10)
  fileCount: number         // int
  totalBytes: number        // long
  createdAtUtc: string      // ISO DateTime string
  downloadsCount: number    // int
}

// FR-011-3: the five search dimensions — q (ONE param; server OR = LinkId exact CI OR EmailRecipient
// LIKE %q%), status word, size range (minBytes/maxBytes pair), date range (from/to pair).
// Pairing XOR is enforced server-side (one without its pair → 400 VALIDATION) — the client only
// sends a pair when both are filled.
export interface SearchParams {
  q?: string
  status?: string           // all/draft/active/expired/download_limit/deleted (absent → all)
  minBytes?: number
  maxBytes?: number
  from?: string             // ISO date — type="date" input value
  to?: string               // ISO date — type="date" input value
  cursor?: string           // opaque server cursor (TA-4.1.4)
}

// TA-4.1.4 pin: default page size 25 — the endpoint rejects limit <=0 or >25 with 400 VALIDATION.
export const DEFAULT_PAGE_SIZE = 25

/**
 * Build the query string for GET /api/v1/admin/transfers. Omit absent keys; always send limit=25
 * (TA-4.1.4 pin — explicit is deterministic). The opaque cursor is forwarded verbatim on page 2+.
 */
export function buildSearchQuery(params: SearchParams): string {
  const sp = new URLSearchParams()
  if (params.q) sp.set('q', params.q)
  if (params.status) sp.set('status', params.status)
  if (params.minBytes !== undefined) sp.set('minBytes', String(params.minBytes))
  if (params.maxBytes !== undefined) sp.set('maxBytes', String(params.maxBytes))
  if (params.from) sp.set('from', params.from)
  if (params.to) sp.set('to', params.to)
  if (params.cursor) sp.set('cursor', params.cursor)
  sp.set('limit', String(DEFAULT_PAGE_SIZE))
  return sp.toString()
}

/**
 * Parse the endpoint-21 GET response into rows + nextCursor. Total: a missing/empty nextCursor is
 * null (last page); rows default to [] so a malformed body can't crash the screen.
 */
export function parseSearchResponse(json: { items?: unknown[]; nextCursor?: string | null }): {
  rows: AdminTransferRowDto[]
  nextCursor: string | null
} {
  const rows = (Array.isArray(json.items) ? json.items : []) as AdminTransferRowDto[]
  return { rows, nextCursor: json.nextCursor ?? null }
}

/**
 * FR-011-3 created column — locale month-day-year + time via toLocaleDateString/toLocaleTimeString
 * (en-US) so the copy is deterministic in tests. Total: unparseable ISO → stable fallback, no throw
 * (same style as overviewData.ts formatUpdatedAt).
 */
export function formatCreatedAt(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return 'Unknown'
  const date = formatDate(iso, { month: 'short' })
  const time = formatTime(iso)
  return `${date}, ${time}`
}
