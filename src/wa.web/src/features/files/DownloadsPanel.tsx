import { useEffect, useState } from 'react'
import type { ReactElement } from 'react'

import i18n from '../../core/i18n/init'
import { PLAN_PATH } from '../billing/planRoute'
import {
  browserLabel,
  countryLabel,
  fetchTransferAnalytics,
  loadAnalyticsAllowed,
  polylinePoints,
  topLabel,
} from './downloadsAnalytics'
import type { TransferAnalyticsDto } from './downloadsAnalytics'

type PanelState =
  | { kind: 'loading' }
  | { kind: 'proRequired' }
  | { kind: 'error' }
  | { kind: 'ok'; data: TransferAnalyticsDto }

const CHART_W = 300
const CHART_H = 60

/**
 * US-023-01 (UI-Reference §5.4, D-7): inline "Downloads" panel under a My Files row.
 * Free plan: "Pro plan required" and NO analytics request (AC-023-3 client half). Pro: daily polyline + three stat chips
 * (total / top country / top browser); zero downloads → "No downloads yet." only (EC-023-1); expired rows get a badge (EC-023-5).
 */
export function DownloadsPanel({ transferId, status }: { transferId: string; status: number }): ReactElement {
  const [state, setState] = useState<PanelState>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    setState({ kind: 'loading' })
    void (async () => {
      const allowed = await loadAnalyticsAllowed()
      if (cancelled) return
      if (!allowed) { setState({ kind: 'proRequired' }); return }
      const result = await fetchTransferAnalytics(transferId)
      if (!cancelled) setState(result)
    })()
    return () => { cancelled = true }
  }, [transferId])

  const expired = status === 2 || status === 3

  return (
    <div className="mf-dl-panel">
      {expired && <span className="mf-chip mf-chip-gray">{i18n.t('files.status.expired')}</span>}
      {state.kind === 'loading' && <p className="mf-loading">{i18n.t('files.analytics.loading')}</p>}
      {state.kind === 'error' && <p role="alert" className="mf-error">{i18n.t('files.analytics.error')}</p>}
      {state.kind === 'proRequired' && (
        <div className="mf-dl-pro">
          <span>{i18n.t('files.analytics.proRequired')}</span>{' '}
          <a className="pf-plan-link" href={PLAN_PATH}>{i18n.t('files.analytics.seePlans')}</a>
        </div>
      )}
      {state.kind === 'ok' && state.data.total === 0 && (
        <p className="mf-dl-empty"><span aria-hidden="true">📥</span> {i18n.t('files.analytics.empty')}</p>
      )}
      {state.kind === 'ok' && state.data.total > 0 && <AnalyticsBody data={state.data} />}
    </div>
  )
}

function AnalyticsBody({ data }: { data: TransferAnalyticsDto }): ReactElement {
  const country = topLabel(data.countries, (c) => c.code)
  const browser = topLabel(data.browsers, (b) => b.family)
  return (
    <>
      <svg className="mf-dl-chart" viewBox={`0 0 ${CHART_W} ${CHART_H}`} role="img" aria-label={i18n.t('files.analytics.chartLabel')} preserveAspectRatio="none">
        <polyline points={polylinePoints(data.daily, CHART_W, CHART_H)} fill="none" stroke="var(--accent)" strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
      </svg>
      <div className="mf-dl-chips">
        <span className="mf-dl-chip">{i18n.t('files.analytics.total')}: <strong>{data.total}</strong></span>
        <span className="mf-dl-chip">{i18n.t('files.analytics.topCountry')}: <strong>{country === null ? '–' : countryLabel(country)}</strong></span>
        <span className="mf-dl-chip">{i18n.t('files.analytics.topBrowser')}: <strong>{browser === null ? '–' : browserLabel(browser)}</strong></span>
      </div>
    </>
  )
}

export default DownloadsPanel
