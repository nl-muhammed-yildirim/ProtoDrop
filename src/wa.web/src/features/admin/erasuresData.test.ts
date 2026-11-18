import { describe, expect, it } from 'vitest'

import type { ErasureRowDto } from './erasuresData'
import {
  CSV_HEADER,
  ERASURE_PAGE_SIZE,
  buildErasuresQuery,
  isLagging,
  parseErasuresResponse,
  toCsv,
} from './erasuresData'

const HOUR = 60 * 60 * 1000
const NOW = Date.parse('2026-11-18T12:00:00.000Z')

function row(over: Partial<ErasureRowDto> = {}): ErasureRowDto {
  return {
    userId: 'a'.repeat(32),
    email: 'dana@x.com',
    deletedAtUtc: '2026-11-18T10:00:00Z',
    reHomedTransferCount: 0,
    sweepStatus: 'pending',
    sweptAtUtc: null,
    ...over,
  }
}

describe('erasuresData (US-047-03)', () => {
  it('buildErasuresQuery_SendsCursorAndLimit', () => {
    expect(buildErasuresQuery(null)).toBe(`limit=${ERASURE_PAGE_SIZE}`)
    expect(buildErasuresQuery('abc=')).toBe(`cursor=abc%3D&limit=${ERASURE_PAGE_SIZE}`)
  })

  it('parseErasuresResponse_MalformedBody_IsEmptyAndNoNext', () => {
    expect(parseErasuresResponse(null)).toEqual({ rows: [], nextCursor: null })
    expect(parseErasuresResponse({ items: 'nope' })).toEqual({ rows: [], nextCursor: null })
    expect(parseErasuresResponse({ items: [row()], nextCursor: 'n' }).nextCursor).toBe('n')
  })

  it('isLagging_Pending49h_True', () => {
    expect(isLagging(row({ deletedAtUtc: new Date(NOW - 49 * HOUR).toISOString() }), NOW)).toBe(true)
  })

  it('isLagging_Pending47h_False', () => {
    expect(isLagging(row({ deletedAtUtc: new Date(NOW - 47 * HOUR).toISOString() }), NOW)).toBe(false)
  })

  it('isLagging_Swept10Days_False', () => {
    expect(isLagging(row({ sweepStatus: 'swept', sweptAtUtc: '2026-11-09T00:00:00Z', deletedAtUtc: new Date(NOW - 240 * HOUR).toISOString() }), NOW)).toBe(false)
  })

  it('toCsv_HeaderRowExact', () => {
    const csv = toCsv([])
    expect(csv.split('\r\n')[0]).toBe('userId,email,deletedAtUtc,reHomedTransferCount,sweepStatus,sweptAtUtc')
    expect(csv.split('\r\n')[0]).toBe(CSV_HEADER)
  })

  it('toCsv_QuotesCommaAndQuote', () => {
    const lines = toCsv([row({ email: 'a"b,c@x.com' })]).split('\r\n')
    expect(lines[1]).toContain('"a""b,c@x.com"')
  })

  it('toCsv_FormulaGuard_PrefixesApostrophe', () => {
    const lines = toCsv([row({ email: '=x@y.z' })]).split('\r\n')
    expect(lines[1]).toContain(",'=x@y.z,")
  })

  it('toCsv_FormulaGuard_NeutralizesLeadingTabAndCr', () => {
    const tab = toCsv([row({ email: '\t=x' })]).split('\r\n')
    expect(tab[1]).toContain(",'\t=x,")
    const cr = toCsv([row({ email: '\rx' })])
    expect(cr).toContain(`,"'\rx",`)
  })

  it('toCsv_NullEmail_IsEmptyCell', () => {
    const lines = toCsv([row({ email: null, reHomedTransferCount: 0 })]).split('\r\n')
    expect(lines[1]).toBe(`${'a'.repeat(32)},,2026-11-18T10:00:00Z,0,pending,`)
  })
})
