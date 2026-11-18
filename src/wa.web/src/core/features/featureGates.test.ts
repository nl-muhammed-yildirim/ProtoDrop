import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { FEATURE_GATE_KEYS, loadFeatureGates, resetFeatureGates, useFeatureGateStore } from './featureGates'

function allOnBody(): Record<string, unknown> {
  return Object.fromEntries(FEATURE_GATE_KEYS.map((k) => [k, true]))
}

beforeEach(() => resetFeatureGates())
afterEach(() => {
  resetFeatureGates()
  vi.unstubAllGlobals()
})

describe('featureGates (US-044-02)', () => {
  it('all six gates are off before any load', () => {
    const state = useFeatureGateStore.getState()
    expect(FEATURE_GATE_KEYS).toHaveLength(6)
    for (const key of FEATURE_GATE_KEYS) expect(state[key]).toBe(false)
  })

  it('success applies the six keys; URL is the features endpoint with credentials', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ ...allOnBody(), 'feature.ads': false }) })
    vi.stubGlobal('fetch', fetchMock)

    await loadFeatureGates()

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:8080/api/v1/features')
    expect(fetchMock.mock.calls[0][1].credentials).toBe('include')
    const state = useFeatureGateStore.getState()
    expect(state['feature.search']).toBe(true)
    expect(state['feature.ads']).toBe(false)
  })

  it('only a literal true turns a gate on; string "true" and unknown keys are ignored', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ 'feature.search': 'true', 'feature.dataExport': 1, 'feature.resend': true, 'feature.analytics': true }),
    }))

    await loadFeatureGates()

    const state = useFeatureGateStore.getState() as unknown as Record<string, boolean>
    expect(state['feature.search']).toBe(false)
    expect(state['feature.dataExport']).toBe(false)
    expect(state['feature.analytics']).toBe(true)
    expect('feature.resend' in state).toBe(false)
  })

  it('non-ok keeps all off and a second load re-fetches', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, json: async () => ({}) })
    vi.stubGlobal('fetch', fetchMock)

    await loadFeatureGates()
    await loadFeatureGates()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    for (const key of FEATURE_GATE_KEYS) expect(useFeatureGateStore.getState()[key]).toBe(false)
  })

  it('network failure keeps all off and a second load re-fetches', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error('offline'))
    vi.stubGlobal('fetch', fetchMock)

    await loadFeatureGates()
    await loadFeatureGates()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    for (const key of FEATURE_GATE_KEYS) expect(useFeatureGateStore.getState()[key]).toBe(false)
  })
})
