import { describe, expect, it } from 'vitest'

import { formatUpdatedAt, statusLabel, statusTone } from './overviewData'

describe('statusLabel (T-011-10)', () => {
  it('statusLabel_ReturnsCanonicalText_ForEveryStatusByte', () => {
    expect(statusLabel(0)).toBe('Draft')
    expect(statusLabel(1)).toBe('Active')
    expect(statusLabel(2)).toBe('Expired')
    expect(statusLabel(3)).toBe('Download limit reached')
    expect(statusLabel(4)).toBe('Deleted')
  })

  it('statusLabel_UnknownByte_ReturnsStringifiedNumber_NeverThrows', () => {
    expect(statusLabel(99)).toBe('99')
    expect(statusLabel(-1)).toBe('-1')
  })

  it('statusTone_MapsActiveGreen_DownloadLimitAmber_OthersGray', () => {
    expect(statusTone(1)).toBe('green')
    expect(statusTone(3)).toBe('amber')
    expect(statusTone(0)).toBe('gray')
    expect(statusTone(2)).toBe('gray')
    expect(statusTone(4)).toBe('gray')
  })
})

describe('formatUpdatedAt (T-011-10)', () => {
  it('formatUpdatedAt_FormatsIsoInEnUsLocale', () => {
    const iso = '2026-11-05T14:30:00.000Z'
    const d = new Date(iso)
    const expected = `${d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}, ${d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}`
    expect(formatUpdatedAt(iso)).toBe(expected)
  })

  it('formatUpdatedAt_UnparseableIso_ReturnsFallback_NoThrow', () => {
    expect(formatUpdatedAt('not-a-date')).toBe('Unknown')
    expect(formatUpdatedAt('')).toBe('Unknown')
  })
})
