import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  countryLabel,
  fetchTransferAnalytics,
  loadAnalyticsAllowed,
  parseAnalytics,
  polylinePoints,
  topLabel,
} from './downloadsAnalytics'

afterEach(() => { vi.unstubAllGlobals() })

describe('parseAnalytics', () => {
  it('junk input yields a safe empty result', () => {
    for (const junk of [null, undefined, 'x', 42, [], {}]) {
      expect(parseAnalytics(junk)).toEqual({ total: 0, daily: [], countries: [], browsers: [] })
    }
  })

  it('drops malformed entries and keeps valid ones', () => {
    const r = parseAnalytics({
      total: 3,
      daily: [{ date: '2026-11-18', count: 3 }, { date: 5, count: 1 }, { date: '2026-11-19', count: -1 }, null],
      countries: [{ code: 'NL', count: 2 }, { code: 'DE' }],
      browsers: [{ family: 'Chrome', count: 3 }, { family: 7, count: 1 }],
    })
    expect(r.total).toBe(3)
    expect(r.daily).toEqual([{ date: '2026-11-18', count: 3 }])
    expect(r.countries).toEqual([{ code: 'NL', count: 2 }])
    expect(r.browsers).toEqual([{ family: 'Chrome', count: 3 }])
  })
})

describe('fetchTransferAnalytics', () => {
  it('403 -> proRequired', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 403 })))
    expect(await fetchTransferAnalytics('abc')).toEqual({ kind: 'proRequired' })
  })

  it('500 and network failure -> error', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 500 })))
    expect(await fetchTransferAnalytics('abc')).toEqual({ kind: 'error' })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('offline') }))
    expect(await fetchTransferAnalytics('abc')).toEqual({ kind: 'error' })
  })

  it('200 -> ok with parsed data', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ total: 1, daily: [], countries: [], browsers: [] }), { status: 200 })))
    expect(await fetchTransferAnalytics('abc')).toEqual({ kind: 'ok', data: { total: 1, daily: [], countries: [], browsers: [] } })
  })
})

describe('loadAnalyticsAllowed', () => {
  it('true only for a literal features.analytics === true', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ features: { analytics: true } }), { status: 200 })))
    expect(await loadAnalyticsAllowed()).toBe(true)
  })

  it('fails closed on 401, network error and a non-boolean value', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 401 })))
    expect(await loadAnalyticsAllowed()).toBe(false)
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('offline') }))
    expect(await loadAnalyticsAllowed()).toBe(false)
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ features: { analytics: 'true' } }), { status: 200 })))
    expect(await loadAnalyticsAllowed()).toBe(false)
  })
})

describe('polylinePoints', () => {
  it('no days -> empty string', () => {
    expect(polylinePoints([], 300, 60)).toBe('')
  })

  it('single day -> flat line across the width', () => {
    expect(polylinePoints([{ date: 'd', count: 4 }], 300, 60)).toBe('0,2 300,2')
  })

  it('all-zero -> baseline', () => {
    const pts = polylinePoints([{ date: 'a', count: 0 }, { date: 'b', count: 0 }, { date: 'c', count: 0 }], 300, 60)
    expect(pts).toBe('0,58 150,58 300,58')
  })

  it('three days: peak at the top, zero at the baseline', () => {
    const pts = polylinePoints([{ date: 'a', count: 0 }, { date: 'b', count: 4 }, { date: 'c', count: 2 }], 300, 60)
    expect(pts).toBe('0,58 150,2 300,30')
  })
})

describe('labels', () => {
  it('"??" -> Unknown, known code -> region name, bad code -> the code', () => {
    expect(countryLabel('??', 'en')).toBe('Unknown')
    expect(countryLabel('NL', 'en')).toBe('Netherlands')
    expect(countryLabel('1', 'en')).toBe('1')
  })

  it('topLabel is the first bucket or null', () => {
    expect(topLabel([], (b: { count: number; k: string }) => b.k)).toBeNull()
    expect(topLabel([{ count: 2, k: 'x' }, { count: 1, k: 'y' }], (b) => b.k)).toBe('x')
  })
})
