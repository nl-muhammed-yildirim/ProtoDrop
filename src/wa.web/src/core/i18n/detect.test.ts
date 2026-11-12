// @vitest-environment jsdom
// T-014-02 (FR-014-3): locale detection order — account setting → browser → en.
//
// init.ts resolves the language at import time, so each scenario installs its
// localStorage/navigator stubs first, then dynamically imports init fresh via
// vi.resetModules() so every case re-runs module init with its own environment.
import { describe, it, expect, vi } from 'vitest'

const ACCOUNT_LOCALE_KEY = 'protoDrop.accountLocale'

function installLocalStorageShim(entries: Record<string, string> = {}): void {
  const store = new Map<string, string>(Object.entries(entries))
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    value: {
      getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
      setItem: (k: string, v: string) => { store.set(k, String(v)) },
      removeItem: (k: string) => { store.delete(k) },
    },
  })
}

function setNavigatorLanguage(language: string | undefined): void {
  Object.defineProperty(window.navigator, 'language', { configurable: true, value: language })
}

async function importInitFresh() {
  vi.resetModules()
  return await import('./init')
}

describe('T-014-02 detection order (FR-014-3)', () => {
  it('account setting wins over browser (AC-014-3)', async () => {
    installLocalStorageShim({ [ACCOUNT_LOCALE_KEY]: 'nl' })
    setNavigatorLanguage('de-DE')
    const mod = await importInitFresh()
    expect(mod.default.language).toBe('nl')
    expect(mod.getResolvedLocale()).toBe(mod.default.language)
  })

  it('guest with supported browser language resolves the base code', async () => {
    installLocalStorageShim({})
    setNavigatorLanguage('de-DE')
    const mod = await importInitFresh()
    expect(mod.default.language).toBe('de')
    expect(mod.getResolvedLocale()).toBe(mod.default.language)
  })

  it('unsupported browser language falls through to en silently', async () => {
    installLocalStorageShim({})
    setNavigatorLanguage('ja-JP')
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    const mod = await importInitFresh()
    expect(mod.default.language).toBe('en')
    expect(mod.getResolvedLocale()).toBe(mod.default.language)
    expect(errorSpy).not.toHaveBeenCalled()
    errorSpy.mockRestore()
  })

  it('no browser signal and no account key resolves en', async () => {
    installLocalStorageShim({})
    setNavigatorLanguage(undefined)
    const mod = await importInitFresh()
    expect(mod.default.language).toBe('en')
    expect(mod.getResolvedLocale()).toBe(mod.default.language)
  })

  it('corrupt account value is ignored and falls through to browser', async () => {
    installLocalStorageShim({ [ACCOUNT_LOCALE_KEY]: 'bogus' })
    setNavigatorLanguage('fr-FR')
    const mod = await importInitFresh()
    expect(mod.default.language).toBe('fr')
    expect(mod.getResolvedLocale()).toBe(mod.default.language)
  })

  it('imports without throwing when localStorage is absent (SSR safety)', async () => {
    // Remove any shim left by earlier cases: this repo's jsdom env has no window.localStorage,
    // and the typeof-window/try-catch guards in init.ts must swallow that. Also clear the
    // navigator stub from prior cases so the only signal is the absent storage.
    delete (window as unknown as Record<string, unknown>).localStorage
    setNavigatorLanguage(undefined)
    const mod = await importInitFresh()
    expect(mod.default.language).toBe('en')
    expect(mod.getResolvedLocale()).toBe(mod.default.language)
  })
})
