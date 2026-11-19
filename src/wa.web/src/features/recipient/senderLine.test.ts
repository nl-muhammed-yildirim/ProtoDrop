import { describe, expect, it } from 'vitest'

import { ANONYMOUS_SENDER, formatSenderLine } from './senderLine'

describe('formatSenderLine (F-PRF-001 US-021-03)', () => {
  it('puts the person first and the org second (AC-021-2)', () => {
    expect(formatSenderLine('Anna', 'Studio Nova')).toBe('Anna · Studio Nova')
  })

  it('shows only the person when the org is not set', () => {
    expect(formatSenderLine('Anna', null)).toBe('Anna')
  })

  it('shows only the org when the person is blank, with no dangling separator', () => {
    expect(formatSenderLine('', 'Studio Nova')).toBe('Studio Nova')
    expect(formatSenderLine('   ', 'Studio Nova')).toBe('Studio Nova')
  })

  it('falls back to Anonymous when both parts are blank', () => {
    expect(formatSenderLine('', null)).toBe(ANONYMOUS_SENDER)
    expect(formatSenderLine('', '  ')).toBe('Anonymous')
  })

  it('trims both parts', () => {
    expect(formatSenderLine('  Anna ', ' Studio Nova ')).toBe('Anna · Studio Nova')
  })
})
