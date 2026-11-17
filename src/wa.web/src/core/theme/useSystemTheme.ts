import { useEffect } from 'react'

import { resolveTheme } from './resolveTheme'

/**
 * T-015-02 (F-TRF-015, FR-015-4 / AC-015-3): live OS theme tracking.
 * Registers a matchMedia('(prefers-color-scheme: dark)') listener ONLY while in system mode — an explicit
 * light/dark account choice never follows the OS (EC-015-1). Each OS change re-resolves via resolveTheme and
 * sets an EXPLICIT data-theme; T-015-01 removed the @media fallback, so there is no :root scope to fall back onto.
 */
export function useSystemTheme(theme: 'system' | 'light' | 'dark') {
  useEffect(() => {
    if (theme !== 'system') return   // EC-015-1: explicit light/dark never follow the OS — no listener is registered for them.
    if (typeof window.matchMedia !== 'function') return   // jsdom/SSR safety — no listener, no throw.
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const onOSChange = () => {
      document.documentElement.setAttribute('data-theme', resolveTheme(undefined, mq.matches))
    }
    mq.addEventListener('change', onOSChange)
    return () => mq.removeEventListener('change', onOSChange)
  }, [theme])
}
