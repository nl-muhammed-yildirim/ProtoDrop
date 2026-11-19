// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'

import { loadSchedulingAllowed, toUtcIso } from './schedule'

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

describe('loadSchedulingAllowed', () => {
  afterEach(() => { vi.unstubAllGlobals() })

  const stub = (status: number, body: unknown): void => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify(body), { status }))
  }

  it('is true only when features.scheduling is true', async () => {
    stub(200, { features: { scheduling: true } })
    expect(await loadSchedulingAllowed()).toBe(true)
  })

  it('is false when the plan says false or the field is missing', async () => {
    stub(200, { features: { scheduling: false } })
    expect(await loadSchedulingAllowed()).toBe(false)
    stub(200, { planCode: 'free' })
    expect(await loadSchedulingAllowed()).toBe(false)
  })

  it('is false on 401 (guest)', async () => {
    stub(401, { code: 'UNAUTHENTICATED' })
    expect(await loadSchedulingAllowed()).toBe(false)
  })

  it('is false on a network error', async () => {
    vi.stubGlobal('fetch', async () => { throw new Error('down') })
    expect(await loadSchedulingAllowed()).toBe(false)
  })
})
