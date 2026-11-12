/**
 * T-014-01 (FR-014-1 / TA-8.5): i18next init + 8 locale resource bundles.
 *
 * Static imports of all 8 bundles (tiny files; no dynamic import(), no http-backend).
 * Synchronous init on import: main.tsx imports this module before createRoot render.
 *
 * T-014-02 (FR-014-3): initial language detection order — account setting (localStorage)
 * → navigator.language → 'en' — resolved synchronously before first render.
 */
import i18next from 'i18next'
import en from './locales/en.json'
import nl from './locales/nl.json'
import fr from './locales/fr.json'
import es from './locales/es.json'
import pt from './locales/pt.json'
import it from './locales/it.json'
import de from './locales/de.json'
import tr from './locales/tr.json'
import { trackClientEvent } from '../telemetry/clientTelemetry'

const resources = {
  en: { translation: en },
  nl: { translation: nl },
  fr: { translation: fr },
  es: { translation: es },
  pt: { translation: pt },
  it: { translation: it },
  de: { translation: de },
  tr: { translation: tr },
}

// FR-014-3 supported set — single source of truth is the resources keys above.
const SUPPORTED_LANGUAGES = new Set(Object.keys(resources))

// Account-locale override key (T-014-03 / F-TRF-008 profile switcher writes it).
const ACCOUNT_LOCALE_KEY = 'protoDrop.accountLocale'

/**
 * T-014-02 (FR-014-3): account setting → browser language → 'en', all synchronous, no network.
 * SSR/test-safe per the core/auth/session.ts pattern: typeof-window guard + try/catch on every read.
 */
export function resolveInitialLanguage(): string {
  if (typeof window === 'undefined') return 'en'
  // 1. Account setting wins over browser (AC-014-3). Missing/invalid/corrupt → fall through.
  try {
    const stored = window.localStorage.getItem(ACCOUNT_LOCALE_KEY)
    if (stored && SUPPORTED_LANGUAGES.has(stored)) return stored
  } catch {
    // localStorage absent or blocked — fall through.
  }
  // 2. Browser language base code, lowercased.
  try {
    const base = (window.navigator.language ?? '').split('-')[0].toLowerCase()
    if (base && SUPPORTED_LANGUAGES.has(base)) return base
  } catch {
    // No usable browser signal — fall through.
  }
  // 3. Default; unsupported codes fall through silently (US-014-02).
  return 'en'
}

const i18n = i18next.createInstance()

i18n.init({
  resources,
  lng: resolveInitialLanguage(),
  fallbackLng: 'en',
  // Probed (T-014-03 / AC-014-2): i18next only emits the missingKey event when saveMissing is set —
  // without it the telemetry handler below never fires. No store mutation of imported bundles observed.
  saveMissing: true,
  interpolation: {
    // React escapes rendered strings; double-escaping would mangle values.
    escapeValue: false,
  },
})

// Dev-only missingKey handler (TA-8.5 / EC-014-1): warn key + locale so T-014-03's
// sweep catches gaps early, plus translation_missing telemetry (AC-014-2).
i18n.on('missingKey', (lngs, _ns, key) => {
  if (import.meta.env.DEV) {
    console.warn(`[i18n] missing key "${key}" for locale(s) ${lngs.join(', ')}`)
  }
  trackClientEvent('translation_missing', { key, locale: i18n.language })
})

// T-025's app.initialized properties read this (no new event — the closed lists don't have it).
export function getResolvedLocale(): string {
  return i18n.language
}

export default i18n
