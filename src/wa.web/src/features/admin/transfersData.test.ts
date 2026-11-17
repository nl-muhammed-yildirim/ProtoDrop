import { describe, expect, it } from 'vitest'

import type { AdminTransferRowDto } from './transfersData'
import { buildSearchQuery, parseSearchResponse } from './transfersData'

// T-011-11 (FR-011-3): pure helper facts — no jsdom needed.

function wireRow(overrides: Partial<AdminTransferRowDto> = {}): AdminTransferRowDto {
  return {
    id: 'a1b2c3d4e5f67890abcdef1234567890',   // Guid 'n'-form (32 hex chars) — the T-011-11 projection addition.
    linkId: 'Ab3dEf5g',
    ownerEmail: 'a@b.c',
    status: 1,
    fileCount: 3,
    totalBytes: 1288490188,
    createdAtUtc: '2026-10-31T12:00:00.000Z',
    downloadsCount: 87,
    ...overrides,
  }
}

describe('buildSearchQuery (T-011-11 / FR-011-3)', () => {
  it('IncludesOnlyPresentKeys — omits absent keys; sends limit=25 by default', () => {
    const qs = buildSearchQuery({ q: 'hello' })
    expect(qs).toBe('q=hello&limit=25')   // only q + limit — no status/minBytes/maxBytes/from/to/cursor.
  })

  it('SendsAllFiveDimensions_WhenPresent', () => {
    const qs = buildSearchQuery({
      q: 'x',
      status: 'active',
      minBytes: 10,
      maxBytes: 20,
      from: '2026-01-01',
      to: '2026-01-31',
    })
    const sp = new URLSearchParams(qs)
    expect(sp.get('q')).toBe('x')
    expect(sp.get('status')).toBe('active')
    expect(sp.get('minBytes')).toBe('10')
    expect(sp.get('maxBytes')).toBe('20')
    expect(sp.get('from')).toBe('2026-01-01')
    expect(sp.get('to')).toBe('2026-01-31')
    expect(sp.get('limit')).toBe('25')
  })

  it('ForwardsOpaqueCursor_Verbatim', () => {
    const qs = buildSearchQuery({ cursor: 'CURSOR_PAGE2' })
    expect(qs).toBe('cursor=CURSOR_PAGE2&limit=25')   // the opaque cursor is forwarded verbatim (TA-4.1.4).
  })
})

describe('parseSearchResponse (T-011-11 / FR-011-3)', () => {
  it('MapsWireRowsToDto — id, linkId, status number, totalBytes', () => {
    const rows = [wireRow()]
    const parsed = parseSearchResponse({ items: rows as unknown[], nextCursor: 'CURSOR_PAGE2' })
    expect(parsed.rows.length).toBe(1)
    expect(parsed.rows[0].id).toBe('a1b2c3d4e5f67890abcdef1234567890')
    expect(parsed.rows[0].linkId).toBe('Ab3dEf5g')
    expect(parsed.rows[0].status).toBe(1)   // byte 0..4 as a JSON number (src/wa.domain/Transfer.cs L10)
    expect(parsed.rows[0].totalBytes).toBe(1288490188)
    expect(parsed.nextCursor).toBe('CURSOR_PAGE2')
  })

  it('NullNextCursor_ReturnsNull', () => {
    const parsed = parseSearchResponse({ items: [], nextCursor: null })
    expect(parsed.rows).toEqual([])
    expect(parsed.nextCursor).toBeNull()   // last page — Next is disabled.
  })

  it('MissingItems_DefaultsToEmptyArray', () => {
    const parsed = parseSearchResponse({})
    expect(parsed.rows).toEqual([])   // a malformed body can't crash the screen.
    expect(parsed.nextCursor).toBeNull()
  })
})
