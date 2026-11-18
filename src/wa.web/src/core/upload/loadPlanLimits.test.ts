import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { loadPlanLimits } from './loadPlanLimits'
import { checkLimits, getPlanLimits, resetPlanLimits } from './planLimits'

const SIX_GIB = 6 * 1024 ** 3
const PRO = 10737418240
const FREE = 5368709120

beforeEach(() => resetPlanLimits())
afterEach(() => {
  resetPlanLimits()
  vi.unstubAllGlobals()
})

describe('loadPlanLimits', () => {
  it('success: applies the plan limits, so a 6 GiB file is within Pro but flagged on Free', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ planCode: 'pro', limits: { maxTransferSize: PRO, maxSingleFile: PRO } }) })
    vi.stubGlobal('fetch', fetchMock)

    expect(checkLimits([{ id: 'a', size: SIX_GIB }], getPlanLimits()).overFileIds.has('a')).toBe(true) // Free default
    expect(await loadPlanLimits()).toBe(true)

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:8080/api/v1/billing/plan')
    expect(fetchMock.mock.calls[0][1].credentials).toBe('include')
    expect(getPlanLimits()).toEqual({ maxTransferSize: PRO, maxSingleFile: PRO })
    const r = checkLimits([{ id: 'a', size: SIX_GIB }], getPlanLimits())
    expect(r.overTotal).toBe(false)
    expect(r.overFileIds.size).toBe(0)
  })

  it('non-ok response keeps the Free defaults', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: async () => ({}) }))
    expect(await loadPlanLimits()).toBe(false)
    expect(getPlanLimits()).toEqual({ maxTransferSize: FREE, maxSingleFile: FREE })
  })

  it('network failure keeps the Free defaults', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    expect(await loadPlanLimits()).toBe(false)
    expect(getPlanLimits()).toEqual({ maxTransferSize: FREE, maxSingleFile: FREE })
  })

  it('response without limits keeps the Free defaults', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ planCode: 'pro' }) }))
    expect(await loadPlanLimits()).toBe(false)
    expect(getPlanLimits()).toEqual({ maxTransferSize: FREE, maxSingleFile: FREE })
  })
})
