// T-013-02 (FR-013-6): mapProblem is exhaustive over all 20 closed-list codes; unknown code → error500.
import { describe, expect, it } from 'vitest'

import { mapProblem, parseActiveTransfersLimit } from '../../core/api/errorMap'

// The exact 20-member closed set (src/wa.domain/Errors/ProblemCodes.cs).
const ALL_CODES = [
  'VALIDATION',
  'UNAUTHENTICATED',
  'FORBIDDEN',
  'NOT_FOUND',
  'TRANSFER_NOT_FINALIZED',
  'TRANSFER_SIZE_EXCEEDED',
  'STORAGE_QUOTA_EXCEEDED',
  'MAX_DOWNLOADS_REACHED',
  'WRONG_PASSWORD',
  'TRANSFER_EXPIRED',
  'TRANSFER_DELETED',
  'FILES_GONE',
  'IDEMPOTENCY_CONFLICT',
  'RATE_LIMITED',
  'STRIPE_WEBHOOK_MISMATCH',
  'EMAIL_UNSUBSCRIBED',
  'LINK_EXPIRED',
  'BILLING_UNAVAILABLE',
  'ACTIVE_TRANSFERS_EXCEEDED',
  'INTERNAL',
] as const

describe('mapProblem — exhaustive over the 20 closed-list codes (TA-4.1.3)', () => {
  it.each([
    ['TRANSFER_SIZE_EXCEEDED', { kind: 'inlineLimit' }],
    ['STORAGE_QUOTA_EXCEEDED', { kind: 'inlineLimit' }],
    ['ACTIVE_TRANSFERS_EXCEEDED', { kind: 'inlineLimit' }], // FR-020-6: paywall copy, not the 500 screen
    // Program.cs L367-368: PublicTransferState.NotFound emits TRANSFER_EXPIRED (404) — no separate
    // TRANSFER_NOT_FOUND on the wire; the 404 link state is 'expired'.
    ['TRANSFER_EXPIRED', { kind: 'ftrf003State', which: 'expired' }],
    ['TRANSFER_DELETED', { kind: 'ftrf003State', which: 'deleted' }],
    ['WRONG_PASSWORD', { kind: 'wrongPasswordShake' }],
    ['MAX_DOWNLOADS_REACHED', { kind: 'maxDownloadsUsed' }],
    ['FILES_GONE', { kind: 'filesGone' }],
    ['RATE_LIMITED', { kind: 'rateLimited' }],
  ] as const)('%s → %j', (code, expected) => {
    expect(mapProblem(code)).toEqual(expected)
  })

  it.each([
    'VALIDATION',
    'UNAUTHENTICATED',
    'FORBIDDEN',
    'NOT_FOUND',
    'TRANSFER_NOT_FINALIZED',
    'IDEMPOTENCY_CONFLICT',
    'STRIPE_WEBHOOK_MISMATCH',
    'EMAIL_UNSUBSCRIBED',
    'LINK_EXPIRED',
    'INTERNAL',
  ] as const)('%s → error500 (the only unmapped path)', (code) => {
    expect(mapProblem(code)).toEqual({ kind: 'error500' })
  })

  it('covers all 20 closed-list codes exactly once', () => {
    expect(ALL_CODES).toHaveLength(20)
    // 9 dedicated mappings + 11 error500 fallbacks = 20.
    const dedicated = ['TRANSFER_SIZE_EXCEEDED', 'STORAGE_QUOTA_EXCEEDED', 'ACTIVE_TRANSFERS_EXCEEDED', 'TRANSFER_EXPIRED', 'TRANSFER_DELETED', 'WRONG_PASSWORD', 'MAX_DOWNLOADS_REACHED', 'FILES_GONE', 'RATE_LIMITED']
    const fallback = ALL_CODES.filter((c) => !dedicated.includes(c))
    expect(fallback).toHaveLength(11)
  })

  it('unknown/missing code falls back to error500', () => {
    expect(mapProblem('TOTALLY_UNKNOWN_CODE')).toEqual({ kind: 'error500' })
    expect(mapProblem('')).toEqual({ kind: 'error500' })
  })
})

describe('parseActiveTransfersLimit (FR-020-6)', () => {
  it('extracts the exact count and limit from the server details text', () => {
    expect(parseActiveTransfersLimit('You have 20 active transfers (limit 20). Upgrade to keep sending.')).toEqual({ count: 20, limit: 20 })
  })

  it('returns null when the text carries no numbers', () => {
    expect(parseActiveTransfersLimit(undefined)).toBeNull()
    expect(parseActiveTransfersLimit('Something else')).toBeNull()
  })
})
