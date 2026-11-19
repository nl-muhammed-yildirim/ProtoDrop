import { getTraceParent } from '../../core/traceparent'

// Fixed-port absolute base, same precedent as loadPlanLimits / PlanPage BILLING_BASE.
const PLAN_URL = 'http://localhost:8080/api/v1/billing/plan'

/**
 * US-022-01 (FR-022-1, EC-022-6): datetime-local value (the sender's local wall clock, no offset) -> UTC ISO string.
 * Null for an empty or unparseable value so the caller can show the inline error instead of sending.
 */
export function toUtcIso(local: string): string | null {
  if (local.trim() === '') return null
  const d = new Date(local)
  if (Number.isNaN(d.getTime())) return null
  return d.toISOString()
}

/**
 * US-022-01 (FR-022-3, AC-022-4): whether the signed-in user's plan allows scheduling, read from GET /billing/plan
 * (features.scheduling mirrors PlanContext.Features). True only on a literal `true`; any failure, 401 (guest) or
 * network error resolves false — the server gate stays the authority.
 */
export async function loadSchedulingAllowed(): Promise<boolean> {
  try {
    const res = await fetch(PLAN_URL, { credentials: 'include', headers: { traceparent: getTraceParent() } })
    if (!res.ok) return false
    const dto = (await res.json()) as { features?: { scheduling?: unknown } }
    return dto.features?.scheduling === true
  } catch {
    return false
  }
}
