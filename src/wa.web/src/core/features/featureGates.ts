import { API_BASE } from '../config/apiBase'
import { create } from 'zustand'

import { getTraceParent } from '../traceparent'

/**
 * US-044-02 (FR-044-7, AC-044-2): client side of the phase gates. GET /api/v1/features (anonymous) returns the six
 * gate booleans keyed by full flag key; only a literal `true` turns a gate on. Fail closed: every gate is off until
 * the read confirms it, and a failed read keeps them off (a later mount retries). The API endpoint filter stays the
 * authority — this only decides whether an entry point is rendered at all (absent, never disabled).
 */

// Fixed-port absolute base, same precedent as loadPlanLimits / PlanPage BILLING_BASE.
const FEATURES_URL = `${API_BASE}/features`

export const FEATURE_GATE_KEYS = [
  'feature.search',
  'feature.dataExport',
  'feature.emailSettings',
  'feature.ads',
  'feature.scheduling',
  'feature.analytics',
] as const

export type FeatureGateKey = (typeof FEATURE_GATE_KEYS)[number]

type GateState = Record<FeatureGateKey, boolean>

function allOff(): GateState {
  return Object.fromEntries(FEATURE_GATE_KEYS.map((k) => [k, false])) as GateState
}

export const useFeatureGateStore = create<GateState>(() => allOff())

let inFlight: Promise<void> | null = null

/** Loads the gate booleans once at a time; failure keeps every gate off and clears the in-flight promise so a later call retries. */
export function loadFeatureGates(): Promise<void> {
  if (inFlight) return inFlight
  inFlight = (async () => {
    try {
      const res = await fetch(FEATURES_URL, { credentials: 'include', headers: { traceparent: getTraceParent() } })
      if (!res.ok) { inFlight = null; return }
      const dto = (await res.json()) as Record<string, unknown>
      const next = allOff()
      for (const key of FEATURE_GATE_KEYS) next[key] = dto[key] === true
      useFeatureGateStore.setState(next)
    } catch {
      inFlight = null
    }
  })()
  return inFlight
}

export function useFeatureGate(key: FeatureGateKey): boolean {
  return useFeatureGateStore((s) => s[key])
}

/** Test helper: back to all-off and forget any in-flight read. */
export function resetFeatureGates(): void {
  inFlight = null
  useFeatureGateStore.setState(allOff())
}
