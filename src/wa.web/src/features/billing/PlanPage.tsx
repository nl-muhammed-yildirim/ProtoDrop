import { useEffect, useState } from 'react'

import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import { formatBytes } from '../../core/upload/formatBytes'
import { meterState, planNameKey, upgradeAction } from './planUsage'
import type { PlanUsageDto } from './planUsage'

// Fixed-port absolute base, same precedent as ProfilePage ME_BASE (TA-4.2 billing routes).
const BILLING_BASE = 'http://localhost:8080/api/v1/billing'

type Interval = 'monthly' | 'annual'

interface PlanPageProps {
  /** Tests inject a spy; the default performs the full-page redirect to Stripe. */
  onRedirect?: (url: string) => void
}

function defaultRedirect(url: string): void {
  window.location.assign(url)
}

interface MeterProps {
  label: string
  used: number
  allowed: number | null
  format: (n: number) => string
  testId: string
}

function Meter({ label, used, allowed, format, testId }: MeterProps) {
  const state = meterState(used, allowed)
  const text = i18n.t('billing.meter.usedOf', {
    used: format(used),
    allowed: allowed === null ? i18n.t('billing.meter.unlimited') : format(allowed),
  })
  const toneClass = state.tone === 'full' ? ' bp-meter-full' : state.tone === 'over' ? ' bp-meter-over' : ''
  const aria =
    allowed === null
      ? { 'aria-valuemin': 0, 'aria-valuetext': text }
      : { 'aria-valuemin': 0, 'aria-valuemax': allowed, 'aria-valuenow': Math.min(used, allowed), 'aria-valuetext': text }
  return (
    <div className={`bp-meter${toneClass}`} role="meter" aria-label={label} data-testid={testId} {...aria}>
      <div className="bp-meter-label">{label}</div>
      <div className="bp-meter-bar"><div className="bp-meter-fill" style={{ width: `${state.percent}%` }} /></div>
      <div className="bp-meter-text">{text}</div>
    </div>
  )
}

/**
 * US-020-03 (UI-Reference §5.7): plan screen at /account/plan (signed-in only).
 * Reads GET /billing/plan (live usage vs. PlanContext limits), renders two meters and the Upgrade CTA
 * (Free: Checkout, Pro: Billing Portal, Business: none).
 */
export function PlanPage({ onRedirect = defaultRedirect }: PlanPageProps) {
  const [usage, setUsage] = useState<PlanUsageDto | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [upgradeFailed, setUpgradeFailed] = useState(false)
  const [upgrading, setUpgrading] = useState(false)
  const [interval, setIntervalValue] = useState<Interval>('monthly')

  useEffect(() => {
    let cancelled = false
    fetch(`${BILLING_BASE}/plan`, { credentials: 'include', headers: { traceparent: getTraceParent() } })
      .then(async (res) => {
        if (!res.ok) throw new Error('load failed')
        const dto = (await res.json()) as PlanUsageDto
        if (!cancelled) setUsage(dto)
      })
      .catch(() => { if (!cancelled) setLoadFailed(true) })
    return () => { cancelled = true }
  }, [])

  async function upgrade() {
    if (!usage) return
    const action = upgradeAction(usage.planCode)
    if (!action) return
    setUpgrading(true)
    setUpgradeFailed(false)
    try {
      let url: string | null = null
      if (action.kind === 'checkout') {
        const res = await fetch(`${BILLING_BASE}/checkout`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
          credentials: 'include',
          body: JSON.stringify({ planCode: action.planCode, interval }),
        })
        if (!res.ok) throw new Error('checkout failed')
        url = ((await res.json()) as { url?: string | null }).url ?? null
      } else {
        const res = await fetch(`${BILLING_BASE}/portal`, { credentials: 'include', headers: { traceparent: getTraceParent() } })
        if (!res.ok) throw new Error('portal failed')
        url = ((await res.json()) as { url?: string | null }).url ?? null
      }
      if (!url) throw new Error('no url')
      onRedirect(url)
    } catch {
      setUpgradeFailed(true)
    } finally {
      setUpgrading(false)
    }
  }

  if (loadFailed) {
    return (
      <div className="bp">
        <h1 className="pf-heading">{i18n.t('billing.plan.heading')}</h1>
        <p role="alert" className="bp-error">{i18n.t('billing.error.loadFailed')}</p>
      </div>
    )
  }
  if (!usage) {
    return (
      <div className="bp">
        <h1 className="pf-heading">{i18n.t('billing.plan.heading')}</h1>
      </div>
    )
  }

  const nameKey = planNameKey(usage.planCode)
  const planName = nameKey ? i18n.t(nameKey) : usage.planCode
  const storage = meterState(usage.storage.usedBytes, usage.storage.allowedBytes)
  const active = meterState(usage.activeTransfers.used, usage.activeTransfers.allowed)
  const atLimit = storage.tone !== 'normal' || active.tone !== 'normal'
  const action = upgradeAction(usage.planCode)

  return (
    <div className="bp">
      <h1 className="pf-heading">{i18n.t('billing.plan.heading')}</h1>
      <div className="bp-card">
        <p className="bp-current">{i18n.t('billing.plan.current', { name: planName })}</p>
        <Meter label={i18n.t('billing.meter.storage')} used={usage.storage.usedBytes} allowed={usage.storage.allowedBytes} format={formatBytes} testId="bp-meter-storage" />
        <Meter label={i18n.t('billing.meter.activeTransfers')} used={usage.activeTransfers.used} allowed={usage.activeTransfers.allowed} format={(n) => String(n)} testId="bp-meter-active" />
        {action && atLimit && <p role="status" className="bp-at-limit">{i18n.t('billing.plan.atLimit')}</p>}
        {action?.kind === 'checkout' && (
          <fieldset className="bp-interval">
            <legend>{i18n.t('billing.plan.intervalLabel')}</legend>
            <label>
              <input type="radio" name="bp-interval" value="monthly" checked={interval === 'monthly'} onChange={() => setIntervalValue('monthly')} />
              {i18n.t('billing.plan.monthly')}
            </label>
            <label>
              <input type="radio" name="bp-interval" value="annual" checked={interval === 'annual'} onChange={() => setIntervalValue('annual')} />
              {i18n.t('billing.plan.annual')}
            </label>
          </fieldset>
        )}
        {action && (
          <button type="button" className={`bp-upgrade${atLimit ? ' bp-upgrade-highlight' : ''}`} disabled={upgrading} onClick={() => void upgrade()}>
            {i18n.t('billing.plan.upgrade')}
          </button>
        )}
        {upgradeFailed && <p role="alert" className="bp-error">{i18n.t('billing.error.upgradeFailed')}</p>}
      </div>
    </div>
  )
}

export default PlanPage
