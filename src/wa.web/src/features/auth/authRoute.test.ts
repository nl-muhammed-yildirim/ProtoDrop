import { describe, expect, it } from 'vitest'

import { ACCOUNT_PATH_RE, matchAccountPath, matchMagicLinkRedeem } from './authRoute'

describe('matchAccountPath — /account route wiring', () => {
  it('matches exact /account and any /account/* (catch-all until T-008-07)', () => {
    expect(matchAccountPath('/account')).toBe(true)
    expect(matchAccountPath('/account/profile')).toBe(true) // catch-all.
  })

  it('rejects non-account paths (anchored regex)', () => {
    expect(matchAccountPath('/accounting')).toBe(false) // /account/ prefix required for /accounting → false.
    expect(matchAccountPath('/')).toBe(false)
    expect(matchAccountPath('/t/abcdefgh')).toBe(false)
  })

  it('re-matches ACCOUNT_PATH_RE so matcher and regex stay in sync', () => {
    expect(ACCOUNT_PATH_RE.test('/account')).toBe(true) // direct re-match.
    expect(ACCOUNT_PATH_RE.test('/account/profile')).toBe(true)
  })
})

describe('matchMagicLinkRedeem � /auth?ml= route wiring', () => {
  it('returns the decoded token for an encoded link', () => {
    expect(matchMagicLinkRedeem('/auth', '?ml=ab%2Bcd%2Fef%3D%3D')).toBe('ab+cd/ef==')
  })

  it('maps a legacy unencoded + (arrives as a space) back to +', () => {
    expect(matchMagicLinkRedeem('/auth', '?ml=ab+cd/ef==')).toBe('ab+cd/ef==')
  })

  it('returns null when ml is missing or empty', () => {
    expect(matchMagicLinkRedeem('/auth', '')).toBeNull()
    expect(matchMagicLinkRedeem('/auth', '?ml=')).toBeNull()
  })

  it('returns null for other paths', () => {
    expect(matchMagicLinkRedeem('/authx', '?ml=x')).toBeNull()
    expect(matchMagicLinkRedeem('/account', '?ml=x')).toBeNull()
  })
})
