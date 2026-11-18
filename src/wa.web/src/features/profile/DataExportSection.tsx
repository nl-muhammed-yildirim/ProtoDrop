import { useCallback, useEffect, useRef, useState } from 'react'

import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import { ME_BASE } from './ProfilePage'

/** US-047-01 (FR-047-3): refresh cadence while an export is processing. Never faster than 5 s; 30 s keeps GET /auth/me/export well inside the 5/min auth rate rule. */
export const EXPORT_POLL_MS = 30_000

type ExportStatus = 'idle' | 'processing' | 'ready' | 'failed'

interface ExportBody {
  state?: string
  downloadUrl?: string
}

function toStatus(state: string | undefined): ExportStatus {
  return state === 'processing' || state === 'ready' || state === 'failed' ? state : 'idle'
}

/**
 * US-047-01 (AC-047-1, EC-047-2, EC-047-6): "My data" section of the profile page — request a GDPR export, watch it prepare,
 * download the zip. Rendered inside FeatureGate feature.dataExport (absent when off). GET 204 = no export yet.
 */
export function DataExportSection() {
  const [status, setStatus] = useState<ExportStatus>('idle')
  const [downloadUrl, setDownloadUrl] = useState<string | null>(null)
  const [inFlightNote, setInFlightNote] = useState(false)
  const [requestError, setRequestError] = useState(false)
  const [busy, setBusy] = useState(false)
  const alive = useRef(true)

  const apply = useCallback((body: ExportBody | null) => {
    setStatus(toStatus(body?.state))
    setDownloadUrl(body?.state === 'ready' && body.downloadUrl ? body.downloadUrl : null)
  }, [])

  const refresh = useCallback(async () => {
    try {
      const res = await fetch(`${ME_BASE}/export`, { credentials: 'include', headers: { traceparent: getTraceParent() } })
      if (!alive.current) return
      if (res.status === 204) apply(null)
      else if (res.ok) apply((await res.json()) as ExportBody)
    } catch {
      // Keep the current state; the next refresh (or a reload) retries.
    }
  }, [apply])

  useEffect(() => {
    alive.current = true
    void refresh()
    return () => { alive.current = false }
  }, [refresh])

  // While processing: re-read on a setTimeout chain; cleared on unmount and on leaving 'processing'.
  useEffect(() => {
    if (status !== 'processing') return
    const timer = setTimeout(() => { void refresh() }, EXPORT_POLL_MS)
    return () => clearTimeout(timer)
  }, [status, refresh])

  async function requestExport() {
    setBusy(true)
    setRequestError(false)
    setInFlightNote(false)
    try {
      const res = await fetch(`${ME_BASE}/export`, {
        method: 'POST',
        credentials: 'include',
        headers: { traceparent: getTraceParent() },
      })
      if (!alive.current) return
      if (res.status === 202) {
        setStatus('processing')
        setDownloadUrl(null)
      } else if (res.status === 409) {
        setStatus('processing')
        setInFlightNote(true)
      } else {
        setRequestError(true)
      }
    } catch {
      if (alive.current) setRequestError(true)
    } finally {
      if (alive.current) setBusy(false)
    }
  }

  const showButton = status !== 'processing'

  return (
    <section className="pf-export">
      <h2 className="pf-label">{i18n.t('profile.export.heading')}</h2>
      <p className="pf-plan">{i18n.t('profile.export.helper')}</p>
      {status === 'processing' && <p className="pf-plan" role="status">{i18n.t('profile.export.preparing')}</p>}
      {inFlightNote && <p className="pf-plan">{i18n.t('profile.export.inFlight')}</p>}
      {status === 'failed' && <p className="pf-error" role="alert">{i18n.t('profile.export.failed')}</p>}
      {status === 'ready' && downloadUrl && (
        <>
          <p className="pf-plan" role="status">{i18n.t('profile.export.ready')}</p>
          <a className="pf-plan-link" href={downloadUrl} download>{i18n.t('profile.export.download')}</a>
          <p className="pf-plan">{i18n.t('profile.export.validity')}</p>
        </>
      )}
      {showButton && (
        <button type="button" className="pf-save-btn" disabled={busy} onClick={() => void requestExport()}>
          {status === 'failed' ? i18n.t('profile.export.retry') : i18n.t('profile.export.button')}
        </button>
      )}
      {requestError && <p role="alert" className="pf-error">{i18n.t('profile.export.error')}</p>}
    </section>
  )
}

export default DataExportSection
