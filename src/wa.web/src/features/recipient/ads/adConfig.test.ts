import { afterEach, describe, expect, it } from 'vitest'

import { getAdConfig, parseAdConfig, setAdConfigForTest } from './adConfig'

afterEach(() => setAdConfigForTest(undefined))

describe('adConfig (fail closed)', () => {
  it('valid pair -> config', () => {
    expect(parseAdConfig('ca-pub-1234567890123456', '1234567890')).toEqual({ client: 'ca-pub-1234567890123456', unit: '1234567890' })
  })

  it.each([
    [undefined, '1234567890'],
    ['ca-pub-1234567890123456', undefined],
    ['', '1234567890'],
    ['ca-pub-1234567890123456', '  '],
    ['pub-123', '1234567890'],
    ['ca-pub-1234567890123456', 'abc'],
    ['ca-pub-1234567890123456"><script>', '1234567890'],
  ])('missing/blank/malformed (%s, %s) -> null', (c, u) => {
    expect(parseAdConfig(c, u)).toBeNull()
  })

  it('getAdConfig with no override and no build-time vars -> null (no slot, no script)', () => {
    expect(getAdConfig()).toBeNull()
  })

  it('override round-trip', () => {
    const cfg = { client: 'ca-pub-1234567890123456', unit: '1234567890' }
    setAdConfigForTest(cfg)
    expect(getAdConfig()).toBe(cfg)
    setAdConfigForTest(null)
    expect(getAdConfig()).toBeNull()
    setAdConfigForTest(undefined)
    expect(getAdConfig()).toBeNull()
  })
})
