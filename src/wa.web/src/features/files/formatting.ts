/**
 * T-009-05 (FR-009-1/2/4/5, AC-009-3 client half, EC-009-4): pure display helpers for the
 * My Files rows. NO React here — unit-tested by formatting.test.ts (row 5 of the plan).
 * Every function is total (never throws) so a bad wire row can't crash the list.
 */

// T-009-01/T-009-02 wire (TA-4.2#14), verified against src/wa.api/Program.cs MapGet("/api/v1/transfers"):
// anonymous return serializes these camelCase fields (System.Text.Json default) — mirror them exactly.
export interface MyTransferRowDto {
  id: string              // Guid in 'n' form (no dashes, 32 hex chars)
  name: string            // the transfer's display name (truncated in the row via CSS + title attr)
  fileCount: number       // int
  totalBytes: number      // long
  status: number          // byte 0..4 as a JSON number
  expiresAtUtc: string    // ISO DateTime string
  downloadsCount: number  // int
  maxDownloads: number    // int
  recipientCount: number  // int; 0 = link-only transfer (EC-009-4)
}

export interface ListTransfersResponse {
  items: MyTransferRowDto[]
  nextCursor: string | null   // opaque cursor (null = last page, TA-4.1.4)
}

// T-009-02 canonical status filter wire values — the client forwards these to ?status= unchanged;
// the server resolves them (Program.cs ResolveStatusFilter). Never re-filter rows client-side.
export type StatusFilter = 'all' | 'active' | 'expired'

// T-009-02 canonical byte map (verified: Program.cs ResolveStatusFilter — active={1}, expired={2,3}):
//   1 → "Active" / green; 3 → "Download limit reached" / amber (US-009-01 edge copy); 2 → "Expired" / gray.
// Unknown byte → defensive gray text, never a throw (a future status can't crash the list).
export function statusChip(status: number): { text: string; tone: 'green' | 'amber' | 'gray' } {
  switch (status) {
    case 1: return { text: 'Active', tone: 'green' }
    case 3: return { text: 'Download limit reached', tone: 'amber' }
    case 2: return { text: 'Expired', tone: 'gray' }
    default: return { text: String(status), tone: 'gray' }
  }
}

/**
 * Locale month-day-year short date via toLocaleDateString (en-US) — so the "Expired {date}" copy is
 * deterministic in tests. Total: an unparseable/empty ISO string falls back to a stable string, no throw.
 */
export function formatShortDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return 'Expired'
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

/**
 * AC-009-3 client half: the row countdown ticks purely from expiresAtUtc — no per-second server calls.
 *   - remaining <= 0 (including exactly now) → { kind: 'expired', text: "Expired {date}" }
 *   - else → { kind: 'countdown', text: largest whole unit only, e.g. "expires in 2 days", "1h 4m" }.
 */
export function formatRemaining(
  expiresAtUtcIso: string,
  nowMs: number,
): { kind: 'countdown'; text: string } | { kind: 'expired'; text: string } {
  const d = new Date(expiresAtUtcIso)
  if (Number.isNaN(d.getTime())) return { kind: 'expired', text: 'Expired' }

  const remaining = d.getTime() - nowMs
  if (remaining <= 0) return { kind: 'expired', text: `Expired ${formatShortDate(expiresAtUtcIso)}` }

  const sec = Math.floor(remaining / 1000)
  const days = Math.floor(sec / 86400)
  const hours = Math.floor((sec % 86400) / 3600)
  const minutes = Math.floor((sec % 3600) / 60)
  const seconds = sec % 60

  if (days >= 1) return { kind: 'countdown', text: `expires in ${days} day${days === 1 ? '' : 's'}` }
  if (hours >= 1) return { kind: 'countdown', text: `expires in ${hours}h ${minutes}m` }
  if (minutes >= 1) return { kind: 'countdown', text: `expires in ${minutes}m ${seconds}s` }
  return { kind: 'countdown', text: `expires in ${seconds}s` }
}

// EC-009-4: a link-only transfer (no named recipients) shows "Link only" in the recipients column.
export function recipientLabel(count: number): string {
  if (count === 0) return 'Link only'
  return String(count)
}

// FR-009-1 row meta: "{count}/{max}" e.g. "87/100".
export function downloadsLabel(count: number, max: number): string {
  return `${count}/${max}`
}
