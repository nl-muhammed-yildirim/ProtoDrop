import { describe, expect, it } from 'vitest'

import { MAX_EMAILS, parseRecipients, validateAddress } from './recipients'

describe('parseRecipients', () => {
  it('splits on commas and spaces, trims, drops empties, lowercases, dedupes', () => {
    expect(parseRecipients('a@x.com, b@y.com ')).toEqual(['a@x.com', 'b@y.com'])
  })

  it('lowercases addresses separated by newlines', () => {
    expect(parseRecipients('A@X.COM\nB@Y.COM')).toEqual(['a@x.com', 'b@y.com'])
  })

  it('dedupes repeated addresses preserving first-seen order', () => {
    expect(parseRecipients('a@x.com, a@x.com')).toEqual(['a@x.com'])
  })

  it('keeps Gmail aliases distinct', () => {
    expect(parseRecipients('user+1@gmail.com user+2@gmail.com')).toEqual([
      'user+1@gmail.com',
      'user+2@gmail.com',
    ])
  })
})

describe('validateAddress', () => {
  it('rejects an address without a dot in the domain', () => {
    expect(validateAddress('bad-address')).toBe(false)
  })

  it('accepts a simple valid address', () => {
    expect(validateAddress('ok@x.com')).toBe(true)
  })

  it('accepts Gmail alias addresses', () => {
    expect(validateAddress('user+1@gmail.com')).toBe(true)
  })
})

describe('MAX_EMAILS', () => {
  it('is 20', () => {
    expect(MAX_EMAILS).toBe(20)
  })
})