// T-014-03 (AC-014-2 / EC-014-1): missing-key telemetry — the dev console.warn + translation_missing
// event wired in init.ts's missingKey handler, verified through the REAL clientTelemetry module.
//
// Design notes:
// - We assert on clientTelemetryLog (the module-level record built for tests) instead of mocking
//   trackClientEvent — this additionally proves 'translation_missing' is in the TA-10.2 closed list
//   (an ad-hoc name would throw under DEV, which vitest sets true).
// - Probed gate: i18next only emits missingKey when saveMissing is set (init.ts) AND the key is
//   fully missing (usedKey=true — absent from every lng in the lookup chain). A key present in the
//   en fallback fires nothing and returns the EN value.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import i18n from './init'
import enBundle from './locales/en.json'
import { clientTelemetryLog } from '../telemetry/clientTelemetry'

describe('T-014-03 missing-key telemetry (AC-014-2 / EC-014-1)', () => {
  beforeEach(() => {
    vi.spyOn(console, 'debug').mockImplementation(() => {}) // trackClientEvent's one-line summary.
    vi.spyOn(console, 'warn').mockImplementation(() => {}) // init.ts dev-only missingKey warn (DEV=true under vitest).
  })

  afterEach(async () => {
    clientTelemetryLog.splice(0) // module-level record — keep suites hermetic.
    vi.restoreAllMocks()
    await i18n.changeLanguage('en') // restore for other suites.
  })

  it('t() of a fully-missing key under de emits translation_missing with key + locale', async () => {
    await i18n.changeLanguage('de')
    i18n.t('nonexistent.missing.key') // missing in every bundle — the event fires synchronously.

    expect(clientTelemetryLog).toHaveLength(1)
    expect(clientTelemetryLog[0].event).toBe('translation_missing')
    expect(clientTelemetryLog[0].props).toEqual({ key: 'nonexistent.missing.key', locale: 'de' })
  })

  it('a key missing in de but present in en still returns the English fallback value (fallbackLng)', async () => {
    await i18n.changeLanguage('de')
    // Simulate a not-yet-swept key: delete it from the de store data, then re-read resources.
    const deStore = i18n.store.data.de.translation as Record<string, string>
    const original = deStore['landing.dropzone.uploadDegraded']
    delete deStore['landing.dropzone.uploadDegraded']
    try {
      await i18n.changeLanguage('de') // re-read resources from the store (probed pattern).
      expect(i18n.t('landing.dropzone.uploadDegraded')).toBe(enBundle['landing.dropzone.uploadDegraded'])
      // No event for fallback-missing keys — only fully-missing ones are telemetry-relevant.
      expect(clientTelemetryLog).toHaveLength(0)
    } finally {
      deStore['landing.dropzone.uploadDegraded'] = original
      await i18n.changeLanguage('en')
    }
  })
})
