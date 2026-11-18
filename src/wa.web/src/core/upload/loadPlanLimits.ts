import { getTraceParent } from '../traceparent'
import { setPlanLimits } from './planLimits'

// Fixed-port absolute base, same precedent as PlanPage BILLING_BASE.
const PLAN_URL = 'http://localhost:8080/api/v1/billing/plan'

/**
 * Loads the signed-in user's per-upload caps from GET /billing/plan (PlanContext.Limits) into planLimits.
 * Best effort: any failure keeps the current (Free) defaults and never blocks the upload page.
 * Resolves true when limits were applied.
 */
export async function loadPlanLimits(): Promise<boolean> {
  try {
    const res = await fetch(PLAN_URL, { credentials: 'include', headers: { traceparent: getTraceParent() } })
    if (!res.ok) return false
    const dto = (await res.json()) as { limits?: { maxTransferSize?: unknown; maxSingleFile?: unknown } }
    const l = dto.limits
    if (!l || typeof l.maxTransferSize !== 'number' || typeof l.maxSingleFile !== 'number') return false
    setPlanLimits({ maxTransferSize: l.maxTransferSize, maxSingleFile: l.maxSingleFile })
    return true
  } catch {
    return false
  }
}
