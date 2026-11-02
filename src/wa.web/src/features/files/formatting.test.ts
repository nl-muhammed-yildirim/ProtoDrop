// T-009-05 (row 5): pure unit tests for the My Files display helpers — no DOM, no jsdom.
import { describe, expect, it } from 'vitest'

import {
  downloadsLabel,
  formatRemaining,
  formatShortDate,
  recipientLabel,
  statusChip,
} from './formatting'

// Fixed baseline (epoch + 10 minutes) so Date.now is deterministic across tests.
const NOW = Date.parse('2026-01-01T00:10:00.000Z')

function iso(msOffset: number): string {
  return new Date(NOW + msOffset).toISOString()
}

describe('statusChip (T-009-02 canonical byte map)', () => {
  it('byte 1 → "Active" / green', () => {
    expect(statusChip(1)).toEqual({ text: 'Active', tone: 'green' })
  })

  it('byte 3 → "Download limit reached" / amber (US-009-01 edge copy)', () => {
    expect(statusChip(3)).toEqual({ text: 'Download limit reached', tone: 'amber' })
  })

  it('byte 2 → "Expired" / gray', () => {
    expect(statusChip(2)).toEqual({ text: 'Expired', tone: 'gray' })
  })

  it('unknown bytes (0 and 4) → defensive gray text, no throw', () => {
    expect(statusChip(0)).toEqual({ text: '0', tone: 'gray' })
    expect(statusChip(4)).toEqual({ text: '4', tone: 'gray' })
  })
})

describe('formatRemaining (AC-009-3 client half)', () => {
  it('far-future expiry → "expires in N days"', () => {
    // NOW + 2d 3h 4m 5s 6s.
    expect(formatRemaining(iso(2 * 86400_000 + 3 * 3600_000 + 4 * 60_000 + 5_000), NOW))
      .toEqual({ kind: 'countdown', text: 'expires in 2 days' })
    expect(formatRemaining(iso(1 * 86400_000), NOW))
      .toEqual({ kind: 'countdown', text: 'expires in 1 day' })
  })

  it('near boundaries → largest whole unit copy (hours/minutes/seconds)', () => {
    // 1h 4m 5s → "1h 4m" (whole hours present).
    expect(formatRemaining(iso(3600_000 + 4 * 60_000 + 5_000), NOW))
      .toEqual({ kind: 'countdown', text: 'expires in 1h 4m' })
    // 4m 5s → "4m 5s" (whole minutes present).
    expect(formatRemaining(iso(4 * 60_000 + 5_000), NOW))
      .toEqual({ kind: 'countdown', text: 'expires in 4m 5s' })
    // 5s → "5s".
    expect(formatRemaining(iso(5_000), NOW))
      .toEqual({ kind: 'countdown', text: 'expires in 5s' })
  })

  it('exactly now → expired (0 ms counts as past)', () => {
    expect(formatRemaining(iso(0), NOW).kind).toBe('expired')
  })

  it('past expiry → "Expired {date}" where the date matches formatShortDate output', () => {
    const isoStr = iso(-86400_000)   // yesterday relative to NOW.
    const result = formatRemaining(isoStr, NOW)
    expect(result.kind).toBe('expired')
    if (result.kind === 'expired') {
      expect(result.text).toBe(`Expired ${formatShortDate(isoStr)}`)
    }
  })

  it('bad/empty ISO string → graceful expired fallback, no throw', () => {
    expect(formatRemaining('', NOW).kind).toBe('expired')
    expect(formatRemaining('not-a-date', NOW).kind).toBe('expired')
  })
})

describe('formatShortDate', () => {
  it('renders locale month-day-year via en-US short date (deterministic)', () => {
    const d = new Date('2026-10-30T00:00:00.000Z')
    expect(formatShortDate(d.toISOString())).toBe(
      d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
    )
  })
})

describe('recipientLabel (EC-009-4)', () => {
  it('0 recipients → "Link only"', () => {
    expect(recipientLabel(0)).toBe('Link only')
  })

  it('1+ recipients → the count as a string', () => {
    expect(recipientLabel(1)).toBe('1')
    expect(recipientLabel(87)).toBe('87')
  })
})

describe('downloadsLabel', () => {
  it('renders "{count}/{max}" e.g. "87/100"', () => {
    expect(downloadsLabel(87, 100)).toBe('87/100')
    expect(downloadsLabel(0, 0)).toBe('0/0')
  })
})
