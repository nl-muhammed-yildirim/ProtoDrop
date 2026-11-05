import { describe, expect, it } from 'vitest'

import { formatCreatedAt } from './adminData'

// T-011-13 (FR-011-5): pure helper facts — no jsdom needed.

describe('adminData.formatCreatedAt (T-011-13)', () => {
  it('formatCreatedAt_ValidIso_ReturnsEnUsDateAndTime', () => {
    const iso = '2026-10-31T12:00:00.000Z'
    const d = new Date(iso)
    const date = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
    const time = d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
    expect(formatCreatedAt(iso)).toBe(`${date}, ${time}`)   // en-US month-day-year + time, deterministic.
  })

  it('formatCreatedAt_Unparseable_ReturnsUnknown — never throws', () => {
    expect(() => formatCreatedAt('not-a-date')).not.toThrow()
    expect(formatCreatedAt('not-a-date')).toBe('Unknown')
  })
})
