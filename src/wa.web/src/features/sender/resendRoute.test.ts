import { describe, expect, it } from 'vitest'

import { matchResendPath, RESEND_PATH_RE } from './resendRoute'

// T-010-03 (FR-010-4): /resend/{originalTransferId} — the GUID is lowercase hex with dashes
// (standard .NET Guid.ToString() output from endpoint 14's My Files rows).

describe('matchResendPath', () => {
  it('valid lowercase GUID → returns the transfer ID', () => {
    const id = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    expect(matchResendPath(`/resend/${id}`)).toBe(id)
  })

  it('missing path → null', () => {
    expect(matchResendPath('/')).toBeNull()
    expect(matchResendPath('/files')).toBeNull()
    expect(matchResendPath('/resend')).toBeNull()
  })

  it('wrong format → null', () => {
    expect(matchResendPath('/resend/not-a-guid')).toBeNull()
    expect(matchResendPath('/resend/1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef')).toBeNull() // 128 chars, no dashes
    expect(matchResendPath('/resend/a1b2c3d4-e5f6-7890-abcd-ef1234567890/extra')).toBeNull()
  })

  it('uppercase → null (GUIDs from .NET are lowercase)', () => {
    expect(matchResendPath('/resend/A1B2C3D4-E5F6-7890-ABCD-EF1234567890')).toBeNull()
  })

  it('RESEND_PATH_RE is anchored — no partial matches', () => {
    expect(RESEND_PATH_RE.test('/resend/a1b2c3d4-e5f6-7890-abcd-ef1234567890')).toBe(true)
    expect(RESEND_PATH_RE.test('x/resend/a1b2c3d4-e5f6-7890-abcd-ef1234567890')).toBe(false)
  })
})
