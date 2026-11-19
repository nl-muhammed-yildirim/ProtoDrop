import { useEffect, useRef, useState } from 'react'

import i18n from '../../core/i18n/init'
import { loadFeatureGates, useFeatureGate } from '../../core/features/featureGates'
import { PLAN_PATH } from '../billing/planRoute'
import { loadSchedulingAllowed } from './schedule'

export type SendMode = 'now' | 'schedule'

interface ScheduleControlProps {
  mode: SendMode
  onModeChange: (mode: SendMode) => void
  /** datetime-local value (sender's local wall clock). */
  value: string
  onValueChange: (value: string) => void
  /** Set by the parent when "Send transfer" was pressed in schedule mode with no usable value. */
  showRequiredError: boolean
}

/**
 * US-022-01 (FR-022-1, FR-022-3, AC-022-4): "Send now / Schedule" toggle + datetime picker on the link screen.
 * Global gate feature.scheduling off -> nothing at all (FR-044-7: absent, never disabled). Gate on -> the plan read
 * decides: Pro+ gets the picker, Free / guest / failed read gets Schedule disabled with "Pro plan required" and a plan link.
 * The server gate in the send handler stays the authority; the window min/max arrives with US-022-03.
 */
export function ScheduleControl({ mode, onModeChange, value, onValueChange, showRequiredError }: ScheduleControlProps) {
  const gateOn = useFeatureGate('feature.scheduling')
  const [allowed, setAllowed] = useState<boolean | null>(null)
  const alive = useRef(true)

  useEffect(() => {
    alive.current = true
    void loadFeatureGates()   // the link screen is the first consumer of the gates on this route; shared single read
    return () => { alive.current = false }
  }, [])

  useEffect(() => {
    if (!gateOn) return
    void loadSchedulingAllowed().then((ok) => { if (alive.current) setAllowed(ok) })
  }, [gateOn])

  // A plan that cannot schedule never stays in schedule mode.
  useEffect(() => {
    if (allowed === false && mode === 'schedule') onModeChange('now')
  }, [allowed, mode, onModeChange])

  if (!gateOn || allowed === null) return null

  const proRequired = i18n.t('link.schedule.proRequired')

  return (
    <div className="ls-schedule">
      <div className="ls-mode" role="group" aria-label={i18n.t('link.schedule.label')}>
        <button
          type="button"
          className="ls-mode-btn"
          aria-pressed={mode === 'now'}
          onClick={() => onModeChange('now')}
        >
          {i18n.t('link.schedule.sendNow')}
        </button>
        <span className="ls-actions-wrap" title={allowed ? undefined : proRequired}>
          <button
            type="button"
            className="ls-mode-btn"
            aria-pressed={mode === 'schedule'}
            disabled={!allowed}
            onClick={() => onModeChange('schedule')}
          >
            {i18n.t('link.schedule.schedule')}
          </button>
        </span>
      </div>
      {!allowed && (
        <p className="ls-recipients-helper">
          {proRequired}{' '}
          <a className="pf-plan-link" href={PLAN_PATH}>{i18n.t('link.schedule.viewPlan')}</a>
        </p>
      )}
      {allowed && mode === 'schedule' && (
        <>
          <label className="ls-sender-label" htmlFor="schedule-at">{i18n.t('link.schedule.label')}</label>
          <input
            id="schedule-at"
            type="datetime-local"
            className="ls-sender-input ls-schedule-input"
            value={value}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => onValueChange(e.target.value)}
            aria-describedby={showRequiredError ? 'schedule-at-error' : undefined}
          />
          <p className="ls-recipients-helper">{i18n.t('link.schedule.helper')}</p>
          {showRequiredError && (
            <p id="schedule-at-error" role="alert" className="ls-sender-error">{i18n.t('link.schedule.required')}</p>
          )}
        </>
      )}
    </div>
  )
}

export default ScheduleControl
