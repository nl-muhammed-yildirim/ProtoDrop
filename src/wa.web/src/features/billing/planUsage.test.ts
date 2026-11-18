import { describe, expect, it } from 'vitest'

import { matchPlanPath, PLAN_PATH } from './planRoute'
import { hasManageBilling, meterState, planNameKey, upgradeAction } from './planUsage'

describe('meterState (US-020-03)', () => {
  it('0/20 is normal at 0%', () => expect(meterState(0, 20)).toEqual({ unlimited: false, percent: 0, tone: 'normal' }))
  it('10/20 is normal at 50%', () => expect(meterState(10, 20)).toEqual({ unlimited: false, percent: 50, tone: 'normal' }))
  it('20/20 is full at 100%', () => expect(meterState(20, 20)).toEqual({ unlimited: false, percent: 100, tone: 'full' }))
  it('100/20 is over, clamped to 100%', () => expect(meterState(100, 20)).toEqual({ unlimited: false, percent: 100, tone: 'over' }))
  it('5/null is unlimited, unfilled', () => expect(meterState(5, null)).toEqual({ unlimited: true, percent: 0, tone: 'normal' }))
})

describe('hasManageBilling (US-019-02)', () => {
  it('only pro', () => {
    expect(hasManageBilling('pro')).toBe(true)
    expect(hasManageBilling('free')).toBe(false)
    expect(hasManageBilling('business')).toBe(false)
    expect(hasManageBilling('enterprise')).toBe(false)
  })
})

describe('upgradeAction (US-020-03)', () => {
  it('free goes to checkout for pro', () => expect(upgradeAction('free')).toEqual({ kind: 'checkout', planCode: 'pro' }))
  it('pro has no upgrade action (hidden once subscribed)', () => expect(upgradeAction('pro')).toBeNull())
  it('business has no CTA', () => expect(upgradeAction('business')).toBeNull())
  it('unknown plan has no CTA', () => expect(upgradeAction('enterprise')).toBeNull())
})

describe('planNameKey (US-020-03)', () => {
  it('maps the three plan codes', () => {
    expect(planNameKey('free')).toBe('billing.plan.nameFree')
    expect(planNameKey('pro')).toBe('billing.plan.namePro')
    expect(planNameKey('business')).toBe('billing.plan.nameBusiness')
  })
  it('unknown code is null', () => expect(planNameKey('enterprise')).toBeNull())
})

describe('matchPlanPath (US-020-03)', () => {
  it('matches exactly /account/plan', () => {
    expect(matchPlanPath(PLAN_PATH)).toBe(true)
    expect(matchPlanPath('/account/plan/')).toBe(false)
    expect(matchPlanPath('/account/profile')).toBe(false)
    expect(matchPlanPath('/account')).toBe(false)
  })
})
