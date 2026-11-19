import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChangeEvent, DragEvent, KeyboardEvent } from 'react'

import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import { formatBytes } from '../../core/upload/formatBytes'
import { PLAN_PATH } from '../billing/planRoute'
import { ME_BASE } from './ProfilePage'

interface LogoBody {
  logoUrl?: string | null
  maxLogoSize?: number
  brandingEnabled?: boolean
}

/**
 * US-021-04 (FR-021-3/4, AC-021-4/5, EC-021-3): organization logo on the profile page — pick or drop an image, preview it, remove it.
 * The size cap comes from the server (GET /auth/me/logo maxLogoSize); the plan gate is the server's brandingEnabled.
 * Renders nothing when the owner read fails.
 */
export function OrgLogoSection({ onBrandingChange }: { onBrandingChange?: (enabled: boolean) => void }) {
  const [loaded, setLoaded] = useState(false)
  const [logoUrl, setLogoUrl] = useState<string | null>(null)
  const [maxLogoSize, setMaxLogoSize] = useState(0)
  const [brandingEnabled, setBrandingEnabled] = useState(false)
  const [busy, setBusy] = useState(false)
  const [errorKey, setErrorKey] = useState<string | null>(null)
  const alive = useRef(true)
  const input = useRef<HTMLInputElement>(null)

  const apply = useCallback((body: LogoBody) => {
    setLogoUrl(body.logoUrl ?? null)
    setMaxLogoSize(body.maxLogoSize ?? 0)
    setBrandingEnabled(body.brandingEnabled === true)
    onBrandingChange?.(body.brandingEnabled === true)   // single GET /auth/me/logo feeds the name section's Pro gate too
  }, [onBrandingChange])

  useEffect(() => {
    alive.current = true
    void (async () => {
      try {
        const res = await fetch(`${ME_BASE}/logo`, { credentials: 'include', headers: { traceparent: getTraceParent() } })
        if (!alive.current || !res.ok) return
        apply((await res.json()) as LogoBody)
        if (alive.current) setLoaded(true)
      } catch {
        // Render nothing; a reload retries.
      }
    })()
    return () => { alive.current = false }
  }, [apply])

  async function upload(file: File) {
    if (file.size > maxLogoSize) {
      setErrorKey('profile.logo.tooLarge')
      return
    }
    setBusy(true)
    setErrorKey(null)
    try {
      const res = await fetch(`${ME_BASE}/logo`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': file.type || 'application/octet-stream', traceparent: getTraceParent() },
        body: file,
      })
      if (!alive.current) return
      if (res.ok) apply((await res.json()) as LogoBody)
      else if (res.status === 422) setErrorKey('profile.logo.tooLarge')
      else if (res.status === 415) setErrorKey('profile.logo.typeInvalid')
      else if (res.status === 403) setErrorKey('profile.logo.proRequired')
      else setErrorKey('profile.logo.error')
    } catch {
      if (alive.current) setErrorKey('profile.logo.error')
    } finally {
      if (alive.current) setBusy(false)
    }
  }

  async function remove() {
    setBusy(true)
    setErrorKey(null)
    try {
      const res = await fetch(`${ME_BASE}/logo`, {
        method: 'DELETE',
        credentials: 'include',
        headers: { traceparent: getTraceParent() },
      })
      if (!alive.current) return
      if (res.ok) setLogoUrl(null)
      else setErrorKey('profile.logo.error')
    } catch {
      if (alive.current) setErrorKey('profile.logo.error')
    } finally {
      if (alive.current) setBusy(false)
    }
  }

  if (!loaded) return null

  const enabled = brandingEnabled && !busy

  function openPicker() {
    if (enabled) input.current?.click()
  }

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      openPicker()
    }
  }

  function onDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault()
    const file = e.dataTransfer?.files?.[0]
    if (enabled && file) void upload(file)
  }

  function onPick(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (file && enabled) void upload(file)
  }

  const limit = formatBytes(maxLogoSize)

  return (
    <section className="pf-logo">
      <h2 className="pf-label">{i18n.t('profile.logo.heading')}</h2>
      <p className="pf-plan">{i18n.t('profile.logo.helper')}</p>
      {logoUrl && <img className="pf-logo-preview" src={logoUrl} alt={i18n.t('profile.logo.previewAlt')} />}
      <div
        className="pf-logo-drop"
        role="button"
        tabIndex={brandingEnabled ? 0 : -1}
        aria-disabled={!brandingEnabled}
        title={brandingEnabled ? undefined : i18n.t('profile.logo.proRequired')}
        onClick={openPicker}
        onKeyDown={onKeyDown}
        onDragOver={(e) => e.preventDefault()}
        onDrop={onDrop}
      >
        {i18n.t('profile.logo.dropHint', { limit })}
      </div>
      <input ref={input} type="file" accept="image/png,image/jpeg,image/webp,image/svg+xml" onChange={onPick} />
      {!brandingEnabled && (
        <p className="pf-plan">
          {i18n.t('profile.logo.proRequired')}{' '}
          <a className="pf-plan-link" href={PLAN_PATH}>{i18n.t('profile.logo.viewPlan')}</a>
        </p>
      )}
      {logoUrl && (
        <button type="button" className="au-ghost-btn" disabled={busy} onClick={() => void remove()}>
          {i18n.t('profile.logo.remove')}
        </button>
      )}
      {errorKey && <p role="alert" className="pf-error">{i18n.t(errorKey, { limit })}</p>}
    </section>
  )
}

export default OrgLogoSection
