import { describe, expect, it } from 'vitest'

import type { AdminUserRowDto } from './usersData'
import { PLANS, buildSearchQuery, parseSearchResponse, planIdForName } from './usersData'

// T-011-12 (FR-011-2): pure helper facts — no jsdom needed.

function wireRow(overrides: Partial<AdminUserRowDto> = {}): AdminUserRowDto {
  return {
    id: 'a1b2c3d4e5f67890abcdef1234567890',   // Guid as a JSON string — the PATCH/DELETE route target.
    email: 'ada@x.com',
    planName: 'Free',
    storageBytesUsed: 1288490188,
    activeTransferCount: 3,
    createdAtUtc: '2026-10-31T12:00:00.000Z',
    ...overrides,
  }
}

describe('buildSearchQuery (T-011-12 / FR-011-2)', () => {
  it('SendsQVerbatim_AndLimit25 — q is ONE param; the typed text goes verbatim (server trims + lowercases)', () => {
    const qs = buildSearchQuery({ q: 'ada@x.com' })
    expect(qs).toBe('q=ada%40x.com&limit=25')   // URLSearchParams encodes @ — the server decodes it back.
  })

  it('OmitsAbsentKeys — no q → only limit=25', () => {
    const qs = buildSearchQuery({})
    expect(qs).toBe('limit=25')   // no q, no cursor — only the TA-4.1.4 pin.
  })

  it('ForwardsOpaqueCursor_Verbatim — the opaque cursor is forwarded verbatim (TA-4.1.4)', () => {
    const qs = buildSearchQuery({ cursor: 'CURSOR_PAGE2' })
    expect(qs).toBe('cursor=CURSOR_PAGE2&limit=25')
  })
})

describe('planIdForName (T-011-12 / FR-011-2)', () => {
  it('MapsSeededNamesToPinnedGuids — Free/Pro/Business → the TA-14.3 pinned GUIDs', () => {
    expect(planIdForName('Free')).toBe('11111111-0000-4000-8000-000000000001')
    expect(planIdForName('Pro')).toBe('11111111-0000-4000-8000-000000000002')
    expect(planIdForName('Business')).toBe('11111111-0000-4000-8000-000000000003')
  })

  it('NullOrUnknown_ReturnsNull — the select renders a disabled "—" placeholder option in that case', () => {
    expect(planIdForName(null)).toBeNull()
    expect(planIdForName('Enterprise')).toBeNull()   // unknown name → null.
  })
})

describe('PLANS (T-011-12 / FR-011-2)', () => {
  it('IsInSortOrder — Free, Pro, Business (the TA-14.3 seeded set, SortOrder 1..3)', () => {
    expect(PLANS.map((p) => p.name)).toEqual(['Free', 'Pro', 'Business'])
  })
})

describe('parseSearchResponse (T-011-12 / FR-011-2)', () => {
  it('MapsWireRows_WithNullablePlanName — planName null is preserved (the wire allows null)', () => {
    const rows = [wireRow(), wireRow({ planName: null })]
    const parsed = parseSearchResponse({ items: rows as unknown[], nextCursor: 'CURSOR_PAGE2' })
    expect(parsed.rows.length).toBe(2)
    expect(parsed.rows[0].email).toBe('ada@x.com')
    expect(parsed.rows[0].planName).toBe('Free')
    expect(parsed.rows[1].planName).toBeNull()   // null preserved — the select renders a disabled "—".
    expect(parsed.rows[0].storageBytesUsed).toBe(1288490188)
    expect(parsed.rows[0].activeTransferCount).toBe(3)
    expect(parsed.nextCursor).toBe('CURSOR_PAGE2')
  })

  it('NullNextCursor_ReturnsNull — last page → Next is disabled', () => {
    const parsed = parseSearchResponse({ items: [], nextCursor: null })
    expect(parsed.rows).toEqual([])
    expect(parsed.nextCursor).toBeNull()
  })

  it('MissingItems_DefaultsToEmptyArray — a malformed body cannot crash the screen', () => {
    const parsed = parseSearchResponse({})
    expect(parsed.rows).toEqual([])
    expect(parsed.nextCursor).toBeNull()
  })
})
