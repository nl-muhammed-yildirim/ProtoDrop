// US-045-01: pure unit tests for the My Files search helpers — no DOM, no jsdom.
import { describe, expect, it } from 'vitest'

import { buildListUrl, normalizeQuery, SEARCH_MAX_LENGTH } from './search'

describe('normalizeQuery (US-045-01)', () => {
  it('trims surrounding whitespace', () => {
    expect(normalizeQuery('  logo ')).toBe('logo')
  })

  it('caps at the server SearchTerm.MaxLength (100) — longer input is sliced, not rejected client-side', () => {
    const long = 'a'.repeat(SEARCH_MAX_LENGTH + 50)   // 150 chars → exactly 100 after slice.
    expect(normalizeQuery(long)).toHaveLength(SEARCH_MAX_LENGTH)
  })

  it('whitespace-only input normalizes to the empty string (no search)', () => {
    expect(normalizeQuery('   ')).toBe('')
  })
})

describe('buildListUrl (US-045-01)', () => {
  const base = 'http://localhost:8080/api/v1'

  it('no q and no cursor → exactly the plain list URL', () => {
    expect(buildListUrl(base, 'all', '', null)).toBe(`${base}/transfers?status=all`)
  })

  it('q is appended after status and percent-encoded (a@b.com → a%40b.com)', () => {
    expect(buildListUrl(base, 'active', 'a@b.com', null)).toBe(`${base}/transfers?status=active&q=a%40b.com`)
  })

  it('q comes before the cursor and both are encoded (the search cursor is opaque base64)', () => {
    const q = '50%_off'   // '%' must be escaped — a literal LIKE operator in the wire.
    const cursor = 'cmFuazM='
    expect(buildListUrl(base, 'expired', q, cursor)).toBe(
      `${base}/transfers?status=expired&q=${encodeURIComponent(q)}&cursor=${encodeURIComponent(cursor)}`,
    )
  })

  it('q plus a null cursor omits the cursor param entirely (page-0 shape)', () => {
    expect(buildListUrl(base, 'all', 'logo', null)).toBe(`${base}/transfers?status=all&q=logo`)
  })
})
