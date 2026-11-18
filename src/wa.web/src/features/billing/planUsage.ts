/**
 * US-020-03 (FR-020-3): pure helpers for the plan screen — no React, no window.
 * PlanUsageDto mirrors GET /api/v1/billing/plan exactly (camelCase; activeTransfers.allowed null = unlimited).
 */

export interface PlanUsageDto {
  planCode: string
  storage: { usedBytes: number; allowedBytes: number }
  activeTransfers: { used: number; allowed: number | null }
}

export interface MeterState {
  unlimited: boolean
  /** 0-100, clamped. */
  percent: number
  /** full = used equals allowed (warning); over = used above allowed after a downgrade (danger). */
  tone: 'normal' | 'full' | 'over'
}

export function meterState(used: number, allowed: number | null): MeterState {
  if (allowed === null) return { unlimited: true, percent: 0, tone: 'normal' }
  if (allowed <= 0) return { unlimited: false, percent: 100, tone: used > allowed ? 'over' : 'full' }
  const percent = Math.min(100, Math.max(0, (used / allowed) * 100))
  const tone = used > allowed ? 'over' : used === allowed ? 'full' : 'normal'
  return { unlimited: false, percent, tone }
}

export type UpgradeAction = { kind: 'checkout'; planCode: 'pro' } | { kind: 'portal' } | null

/** Free goes to Checkout, Pro to the Billing Portal (checkout refuses paid users), Business / unknown: no CTA. */
export function upgradeAction(planCode: string): UpgradeAction {
  if (planCode === 'free') return { kind: 'checkout', planCode: 'pro' }
  if (planCode === 'pro') return { kind: 'portal' }
  return null
}

export function planNameKey(planCode: string): 'billing.plan.nameFree' | 'billing.plan.namePro' | 'billing.plan.nameBusiness' | null {
  switch (planCode) {
    case 'free': return 'billing.plan.nameFree'
    case 'pro': return 'billing.plan.namePro'
    case 'business': return 'billing.plan.nameBusiness'
    default: return null
  }
}
