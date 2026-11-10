/**
 * T-014-01 (FR-014-1 / TA-8.5): i18next init + 8 locale resource bundles.
 *
 * Static imports of all 8 bundles (tiny files; no dynamic import(), no http-backend —
 * T-014-02's detection order only swaps which preloaded bundle is active).
 * Synchronous init on import: main.tsx imports this module before createRoot render.
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

/**
 * STUB — T-014-02 replaces this with account-setting → browser → en detection.
 * Returns 'en' unconditionally; no storage read yet (repo jsdom env lacks window.localStorage).
 */
export function resolveInitialLanguage(): string {
  return 'en'
}

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

const i18n = i18next.createInstance()

i18n.init({
  resources,
  lng: resolveInitialLanguage(),
  fallbackLng: 'en',
  interpolation: {
    // React escapes rendered strings; double-escaping would mangle values.
    escapeValue: false,
  },
})

// Dev-only missingKey handler (TA-8.5 / EC-014-1): warn key + locale so T-014-03's
// sweep catches gaps early. translation_missing telemetry wiring is T-014-03 scope.
i18n.on('missingKey', (lngs, _ns, key) => {
  if (import.meta.env.DEV) {
    console.warn(`[i18n] missing key "${key}" for locale(s) ${lngs.join(', ')}`)
  }
})

export default i18n
