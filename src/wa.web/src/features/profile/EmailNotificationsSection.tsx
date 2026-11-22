import { useEffect, useRef, useState } from 'react'

import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import { toastRole } from '../../core/ui/toastRole'
import { ME_BASE } from './ProfilePage'

interface MeBody {
  emailNotifications?: boolean
}

/**
 * F-XCT-003 (FR-046-2/4, US-046-01): Notification preferences — one switch, saved on change (PATCH /auth/me { emailNotifications }).
 * Rendered inside FeatureGate feature.emailSettings (absent when off). Renders nothing until the GET /auth/me read succeeds.
 */
export function EmailNotificationsSection() {
  const [loaded, setLoaded] = useState(false)
  const [on, setOn] = useState(true)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [failed, setFailed] = useState(false)
  const alive = useRef(true)

  useEffect(() => {
    alive.current = true
    void (async () => {
      try {
        const res = await fetch(ME_BASE, { credentials: 'include', headers: { traceparent: getTraceParent() } })
        if (!alive.current || !res.ok) return
        const body = (await res.json()) as MeBody
        if (!alive.current) return
        setOn(body.emailNotifications !== false) // effective value; absent = on (FR-046-1)
        setLoaded(true)
      } catch {
        // Render nothing; a reload retries.
      }
    })()
    return () => { alive.current = false }
  }, [])

  async function toggle(next: boolean) {
    const previous = on
    setOn(next)
    setSaving(true)
    setSaved(false)
    setFailed(false)
    try {
      const res = await fetch(ME_BASE, {
        method: 'PATCH',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
        body: JSON.stringify({ emailNotifications: next }),
      })
      if (!alive.current) return
      if (!res.ok) {
        setOn(previous)
        setFailed(true)
        return
      }
      const body = (await res.json()) as MeBody
      if (!alive.current) return
      setOn(body.emailNotifications !== false) // server-wins
      setSaved(true)
    } catch {
      if (alive.current) {
        setOn(previous)
        setFailed(true)
      }
    } finally {
      if (alive.current) setSaving(false)
    }
  }

  if (!loaded) return null

  return (
    <section className="pf-notify">
      <h2 className="pf-label">{i18n.t('profile.notifications.heading')}</h2>
      <label className="pf-switch" htmlFor="pf-notify-switch">
        <input
          id="pf-notify-switch"
          className="pf-switch-input"
          type="checkbox"
          role="switch"
          checked={on}
          disabled={saving}
          onChange={(e) => void toggle(e.target.checked)}
        />
        <span>{i18n.t('profile.notifications.label')}</span>
      </label>
      <p className="pf-plan">{i18n.t('profile.notifications.helper')}</p>
      {saved && <p role={toastRole('success')} className="dz-toast">{i18n.t('profile.notifications.saved')}</p>}
      {failed && <p role={toastRole('error')} className="pf-error">{i18n.t('profile.notifications.error')}</p>}
    </section>
  )
}

export default EmailNotificationsSection
