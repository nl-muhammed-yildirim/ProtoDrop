// T-014-01 (FR-014-1 / TA-8.5): i18next init + 8 locale resource bundles — unit tests.
import { describe, it, expect } from 'vitest'
import i18n, { resolveInitialLanguage } from './init'
import enBundle from './locales/en.json'
import nlBundle from './locales/nl.json'
import frBundle from './locales/fr.json'
import esBundle from './locales/es.json'
import ptBundle from './locales/pt.json'
import itBundle from './locales/it.json'
import deBundle from './locales/de.json'
import trBundle from './locales/tr.json'

const bundles: Record<string, Record<string, string>> = {
  en: enBundle,
  nl: nlBundle,
  fr: frBundle,
  es: esBundle,
  pt: ptBundle,
  it: itBundle,
  de: deBundle,
  tr: trBundle,
}

// TA-8.5 scheme: {area}.{screen}.{control} — exactly 3 segments (camelCase allowed).
const KEY_SCHEME = /^[a-z][a-zA-Z0-9]*\.[a-z][a-zA-Z0-9]*\.[a-z][a-zA-Z0-9]*$/

describe('T-014-01 locale bundles', () => {
  it('all 8 bundles import and parse with the same key set as en', () => {
    const enKeys = Object.keys(enBundle).sort()
    expect(enKeys.length).toBeGreaterThan(0)
    for (const [lng, bundle] of Object.entries(bundles)) {
      expect(Object.keys(bundle).sort(), `key set for ${lng}`).toEqual(enKeys)
    }
  })

  it('every key matches the {area}.{screen}.{control} 3-segment scheme', () => {
    for (const [lng, bundle] of Object.entries(bundles)) {
      for (const key of Object.keys(bundle)) {
        expect(key, `${lng}: ${key}`).toMatch(KEY_SCHEME)
      }
    }
  })

  it('stub resolver returns en', () => {
    expect(resolveInitialLanguage()).toBe('en')
  })
})

describe('T-014-01 i18next instance', () => {
  it('t() returns English for a seed key at lng en', () => {
    expect(i18n.language).toBe('en')
    expect(i18n.t('landing.dropzone.uploadDegraded')).toBe(enBundle['landing.dropzone.uploadDegraded'])
  })

  it('at lng de returns the German value', async () => {
    await i18n.changeLanguage('de')
    expect(i18n.t('landing.dropzone.uploadDegraded')).toBe(deBundle['landing.dropzone.uploadDegraded'])
    // Restore for other suites.
    await i18n.changeLanguage('en')
  })

  it('non-en t() resolves its own bundle and is never blank', async () => {
    await i18n.changeLanguage('de')
    const value = i18n.t('landing.dropzone.uploadDegraded') as string
    // Non-en locale resolves its own bundle (non-blank); fallbackLng en (set in init.ts)
    // guarantees any future missing key still lands on English rather than an empty string.
    expect(value).toBe(deBundle['landing.dropzone.uploadDegraded'])
    expect(value).not.toBe('')
    await i18n.changeLanguage('en')
  })
})
