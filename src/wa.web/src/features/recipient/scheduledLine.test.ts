import { describe, expect, it } from 'vitest'

import i18n from '../../core/i18n/init'
import { formatScheduledWhen, isPreDue } from './scheduledLine'

describe('isPreDue (F-PRF-002 FR-022-6)', () => {
  const now = Date.parse('2026-12-01T12:00:00Z')

  it('is true for a future due time', () => {
    expect(isPreDue('2026-12-01T14:00:00Z', now)).toBe(true)
  })

  it('is false for a past due time', () => {
    expect(isPreDue('2026-12-01T10:00:00Z', now)).toBe(false)
  })

  it('is false when due equals now', () => {
    expect(isPreDue('2026-12-01T12:00:00Z', now)).toBe(false)
  })

  it('is false for null and for garbage', () => {
    expect(isPreDue(null, now)).toBe(false)
    expect(isPreDue('not-a-date', now)).toBe(false)
  })
})

describe('formatScheduledWhen (EC-022-6)', () => {
  it('formats the UTC value in the viewer locale with date, time and zone label', () => {
    const iso = '2026-12-01T14:30:00Z'
    const expected = new Intl.DateTimeFormat(i18n.language, {
      month: '2-digit', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZoneName: 'short',
    }).format(new Date(iso))
    const actual = formatScheduledWhen(iso)
    expect(actual).not.toBe('')
    expect(actual).toBe(expected)
  })
})
