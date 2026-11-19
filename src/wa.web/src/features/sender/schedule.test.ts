// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'

import { checkSchedule, loadSchedulingPlan, toUtcIso } from './schedule'

describe('toUtcIso', () => {
  it('converts a datetime-local value to the matching UTC instant', () => {
    const local = '2026-12-01T10:30'
    expect(toUtcIso(local)).toBe(new Date(local).toISOString())
    expect(toUtcIso(local)).toMatch(/Z$/)
  })

  it('returns null for empty or garbage input', () => {
    expect(toUtcIso('')).toBeNull()
    expect(toUtcIso('   ')).toBeNull()
    expect(toUtcIso('next tuesday')).toBeNull()
  })
})

describe('loadSchedulingPlan', () => {
  afterEach(() => { vi.unstubAllGlobals() })

  const stub = (status: number, body: unknown): void => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify(body), { status }))
  }
  const closed = { allowed: false, window: null }

  it('is allowed with the window only when features.scheduling is true and scheduleWindow is present', async () => {
    stub(200, { features: { scheduling: true }, scheduleWindow: { minMinutes: 5, maxDays: 14 } })
    expect(await loadSchedulingPlan()).toEqual({ allowed: true, window: { minMinutes: 5, maxDays: 14 } })
  })

  it('is closed when the plan says false or the field is missing', async () => {
    stub(200, { features: { scheduling: false }, scheduleWindow: { minMinutes: 5, maxDays: 14 } })
    expect(await loadSchedulingPlan()).toEqual(closed)
    stub(200, { planCode: 'free' })
    expect(await loadSchedulingPlan()).toEqual(closed)
  })

  it('is closed when the window is missing or garbage (fail closed)', async () => {
    stub(200, { features: { scheduling: true } })
    expect(await loadSchedulingPlan()).toEqual(closed)
    stub(200, { features: { scheduling: true }, scheduleWindow: { minMinutes: 'x', maxDays: 14 } })
    expect(await loadSchedulingPlan()).toEqual(closed)
    stub(200, { features: { scheduling: true }, scheduleWindow: { minMinutes: 5, maxDays: 0 } })
    expect(await loadSchedulingPlan()).toEqual(closed)
  })

  it('is closed on 401 (guest)', async () => {
    stub(401, { code: 'UNAUTHENTICATED' })
    expect(await loadSchedulingPlan()).toEqual(closed)
  })

  it('is closed on a network error', async () => {
    vi.stubGlobal('fetch', async () => { throw new Error('down') })
    expect(await loadSchedulingPlan()).toEqual(closed)
  })
})

describe('checkSchedule', () => {
  const w = { minMinutes: 5, maxDays: 14 }
  const now = new Date(2026, 11, 1, 10, 0, 0, 0).getTime()   // whole minute, local
  const pad = (n: number): string => String(n).padStart(2, '0')
  const local = (ms: number): string => {
    const d = new Date(ms)
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
  }
  const MIN = 60_000
  const DAY = 86_400_000

  it('+2 min is too soon (AC-022-2)', () => { expect(checkSchedule(local(now + 2 * MIN), now, w)).toBe('tooSoon') })
  it('+30 days is too late (AC-022-3)', () => { expect(checkSchedule(local(now + 30 * DAY), now, w)).toBe('tooLate') })
  it('exactly +5 min is accepted', () => { expect(checkSchedule(local(now + 5 * MIN), now, w)).toBeNull() })
  it('exactly +14 days is accepted', () => { expect(checkSchedule(local(now + 14 * DAY), now, w)).toBeNull() })
  it('+4 min is too soon', () => { expect(checkSchedule(local(now + 4 * MIN), now, w)).toBe('tooSoon') })
  it('+14 days +1 min is too late', () => { expect(checkSchedule(local(now + 14 * DAY + MIN), now, w)).toBe('tooLate') })
  it('empty is required', () => { expect(checkSchedule('', now, w)).toBe('required') })
})
