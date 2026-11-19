import { useEffect, useRef, useState } from 'react'

import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import { PLAN_PATH } from '../billing/planRoute'
import { ME_BASE } from './ProfilePage'

interface MeBody {
  orgName?: string | null
}

// Mirrors the server's ORG_NAME_MAX_LENGTH (AppUser.OrgNameMaxLength); the server stays the authority.
const ORG_NAME_MAX_LENGTH = 60

/**
 * F-PRF-001 (FR-021-1 / FR-021-5): organization name on the profile page. Read from GET /auth/me, saved with
 * PATCH /auth/me { orgName } (blank clears it); the session profile carries no orgName, hence the one read here.
 * Pro gate = the server's brandingEnabled (GET /auth/me/logo, passed down by ProfilePage): otherwise the input is disabled
 * with the same "Pro plan required" copy the logo section uses. Renders nothing when the read fails.
 */
export function OrgNameSection({ brandingEnabled }: { brandingEnabled: boolean | null }) {
  const [loaded, setLoaded] = useState(false)
  const [saved, setSaved] = useState('')
  const [value, setValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [errorKey, setErrorKey] = useState<string | null>(null)
  const alive = useRef(true)

  useEffect(() => {
    alive.current = true
    void (async () => {
      try {
        const res = await fetch(ME_BASE, { credentials: 'include', headers: { traceparent: getTraceParent() } })
        if (!alive.current || !res.ok) return
        const body = (await res.json()) as MeBody
        if (!alive.current) return
        const current = body.orgName ?? ''
        setSaved(current)
        setValue(current)
        setLoaded(true)
      } catch {
        // Render nothing; a reload retries.
      }
    })()
    return () => { alive.current = false }
  }, [])

  async function save() {
    const trimmed = value.trim()
    setSaving(true)
    setErrorKey(null)
    try {
      const res = await fetch(ME_BASE, {
        method: 'PATCH',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
        body: JSON.stringify({ orgName: trimmed === '' ? null : trimmed }),
      })
      if (!alive.current) return
      if (res.ok) {
        const body = (await res.json()) as MeBody
        const next = body.orgName ?? ''
        setSaved(next)
        setValue(next)
      } else {
        setErrorKey(res.status === 400 ? 'profile.orgName.invalid' : 'profile.orgName.error')
      }
    } catch {
      if (alive.current) setErrorKey('profile.orgName.error')
    } finally {
      if (alive.current) setSaving(false)
    }
  }

  // Pro = the server's Features.Branding (same source as the logo uploader); unknown (logo read pending/failed) renders nothing.
  if (!loaded || brandingEnabled === null) return null
  const pro = brandingEnabled

  return (
    <section className="pf-org">
      <label className="pf-label" htmlFor="pf-org-name">{i18n.t('profile.orgName.label')}</label>
      <p className="pf-plan">{i18n.t('profile.orgName.helper')}</p>
      <input
        id="pf-org-name"
        className="pf-input"
        type="text"
        value={value}
        maxLength={ORG_NAME_MAX_LENGTH}
        disabled={!pro || saving}
        title={pro ? undefined : i18n.t('profile.logo.proRequired')}
        onChange={(e) => setValue(e.target.value)}
      />
      <button
        type="button"
        className="pf-save-btn"
        disabled={!pro || saving || value.trim() === saved}
        onClick={() => void save()}
      >
        {i18n.t('profile.button.save')}
      </button>
      {!pro && (
        <p className="pf-plan">
          {i18n.t('profile.logo.proRequired')}{' '}
          <a className="pf-plan-link" href={PLAN_PATH}>{i18n.t('profile.logo.viewPlan')}</a>
        </p>
      )}
      {errorKey && <p role="alert" className="pf-error">{i18n.t(errorKey, { max: ORG_NAME_MAX_LENGTH })}</p>}
    </section>
  )
}

export default OrgNameSection
