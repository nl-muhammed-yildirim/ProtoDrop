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

export interface ScheduleWindow { minMinutes: number; maxDays: number }
export type ScheduleError = 'required' | 'tooSoon' | 'tooLate' | 'outOfWindow'
export interface SchedulingPlan { allowed: boolean; window: ScheduleWindow | null }

const MS_PER_MINUTE = 60_000
const MS_PER_DAY = 86_400_000

/**
 * US-022-03 (FR-022-2, AC-022-2/3): submit-time window check, inclusive on both ends. The window values come from the
 * server (GET /billing/plan scheduleWindow); the server re-validates with its own clock.
 */
export function checkSchedule(local: string, nowMs: number, w: ScheduleWindow): Exclude<ScheduleError, 'outOfWindow'> | null {
  const iso = toUtcIso(local)
  if (iso === null) return 'required'
  const t = Date.parse(iso)
  if (t < nowMs + w.minMinutes * MS_PER_MINUTE) return 'tooSoon'
  if (t > nowMs + w.maxDays * MS_PER_DAY) return 'tooLate'
  return null
}

function isPositive(n: unknown): n is number {
  return typeof n === 'number' && Number.isFinite(n) && n > 0
}

const NOT_ALLOWED: SchedulingPlan = { allowed: false, window: null }

/**
 * US-022-01 (FR-022-3, AC-022-4) + US-022-03 (D-4): whether the signed-in user's plan allows scheduling, and the window,
 * both read from GET /billing/plan. Allowed only on a literal `features.scheduling === true` AND a window of two positive
 * finite numbers; any failure, 401 (guest) or network error fails closed — the server gate stays the authority.
 */
export async function loadSchedulingPlan(): Promise<SchedulingPlan> {
  try {
    const res = await fetch(PLAN_URL, { credentials: 'include', headers: { traceparent: getTraceParent() } })
    if (!res.ok) return NOT_ALLOWED
    const dto = (await res.json()) as {
      features?: { scheduling?: unknown }
      scheduleWindow?: { minMinutes?: unknown; maxDays?: unknown }
    }
    const w = dto.scheduleWindow
    if (dto.features?.scheduling !== true || !w || !isPositive(w.minMinutes) || !isPositive(w.maxDays)) return NOT_ALLOWED
    return { allowed: true, window: { minMinutes: w.minMinutes, maxDays: w.maxDays } }
  } catch {
    return NOT_ALLOWED
  }
}
