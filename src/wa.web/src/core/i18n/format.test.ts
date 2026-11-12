// T-014-03 (AC-014-4 / FR-014-4): locale-aware date/time via Intl.DateTimeFormat(i18n.language, ...).
// Pinned against Node full-ICU output (probed 2026-11-10): de → "23.08.2026" / "14:30", en → "08/23/2026".
import { afterEach, describe, expect, it } from 'vitest'

import i18n from './init'
import { formatDate, formatTime } from './format'

describe('T-014-03 localized dates (AC-014-4)', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en') // restore for other suites.
  })

  it('formats a date in de as d.M.y ("23.08.2026")', async () => {
    await i18n.changeLanguage('de')
    expect(formatDate('2026-08-23T00:00:00Z')).toBe('23.08.2026') // AC-014-4's exact assertion.
  })

  it('formats a date in en as M/D/YYYY ("08/23/2026")', () => {
    expect(formatDate('2026-08-23T00:00:00Z')).toBe('08/23/2026')
  })

  it('formats a time in de as HH:mm ("14:30")', async () => {
    await i18n.changeLanguage('de')
    // No-offset ISO parses as local time — round-trips deterministically on any host TZ.
    expect(formatTime('2026-08-23T14:30:00')).toBe('14:30')
  })

  it('formats a time in en with AM/PM ("02:30 PM")', () => {
    expect(formatTime('2026-08-23T14:30:00')).toBe('02:30 PM')
  })

  it('returns "" for an unparseable ISO string in every locale', async () => {
    expect(formatDate('not-a-date')).toBe('')
    await i18n.changeLanguage('de')
    expect(formatTime('also-not-a-date')).toBe('')
  })
})
