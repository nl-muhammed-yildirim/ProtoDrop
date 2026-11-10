/**
 * T-011-10 (FR-011-2/6): endpoint 20 types + pure display helpers for the admin Overview screen.
 * NO React here — unit-tested by overviewData.test.ts. Every function is total (never throws) so a
 * bad wire row can't crash the screen. Do NOT import across features — admin owns its own helpers.
 */

// T-011-03 wire shape (TA-4.2#20), verified against src/wa.api/Program.cs L513–L521:
// GET /api/v1/admin/overview returns exactly these camelCase fields — mirror them, no more.
export interface RecentTransferRow {
  linkId: string            // 8-char Crockford
  ownerEmail: string | null // nullable — null → "—" in the table
  status: number            // byte 0..4 as a JSON number (src/wa.domain/Transfer.cs L10)
  totalBytes: number        // long
  createdAtUtc: string      // ISO DateTime string
}

export interface OverviewDto {
  activeTransfers: number
  storageBytesUsed: number
  newUsersToday: number
  emailsSent7d: number
  emailsFailed7d: number
  // T-013-05 (AC-013-3): email health surface — pending/failed totals + last successful delivery + broker DLQ count.
  emailPendingCount: number
  emailFailedTotal: number
  lastSuccessfulDeliveryAge: string | null // nullable — null when nothing was ever delivered
  dlqCount: number
  snapshotAtUtc: string     // ISO DateTime — the FR-011-6 staleness line states this, no client math
  recentTransfers: RecentTransferRow[]
}

// Status byte map (src/wa.domain/Transfer.cs L10): 0=Draft, 1=Active, 2=Expired, 3=DownloadLimit, 4=Deleted.
// Read-only display — unknown byte → String(status), never throws (same defensive style as files/statusChip).
export function statusLabel(status: number): string {
  switch (status) {
    case 0: return 'Draft'
    case 1: return 'Active'
    case 2: return 'Expired'
    case 3: return 'Download limit reached'
    case 4: return 'Deleted'
    default: return String(status)
  }
}

// Tone for the status chip — text + color (color never the only signal; the chip carries its label).
export function statusTone(status: number): 'green' | 'amber' | 'gray' {
  switch (status) {
    case 1: return 'green'
    case 3: return 'amber'
    default: return 'gray'
  }
}

/**
 * FR-011-6: "Updated {time}" line — locale month-day-year + time via toLocaleDateString/toLocaleTimeString
 * (en-US) so the copy is deterministic in tests. Total: unparseable ISO → stable fallback, no throw.
 */
export function formatUpdatedAt(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return 'Unknown'
  const date = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
  const time = d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
  return `${date}, ${time}`
}

/**
 * T-013-05 (AC-013-3): "Last successful delivery" line — locale month-day-year + time (en-US) so the copy is
 * deterministic in tests. Total: null or unparseable ISO → stable fallback, no throw (same defensive style as formatUpdatedAt).
 */
export function formatLastDelivery(iso: string | null): string {
  if (!iso) return 'Never'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return 'Unknown'
  const date = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
  const time = d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
  return `${date}, ${time}`
}
