/**
 * T-015-02 (F-TRF-015, FR-015-4): test-only matchMedia polyfill — jsdom has no window.matchMedia.
 * Installs a controllable stub via Object.defineProperty (removable between tests), mirroring the
 * localStorage-shim pattern in App.test.tsx / ProfilePage.test.tsx. Imported only from .test.* files,
 * so production code never sees it; useSystemTheme's typeof guard keeps non-stubbed environments safe.
 */

type Listener = () => void

export interface MatchMediaStub {
  /** The current matches flag (true → OS prefers dark). */
  readonly matches: boolean
  /** Flip the stub's matches flag (true → OS prefers dark). */
  flipMatches: (dark: boolean) => void
  /** Fire the 'change' event to every registered listener — simulates an OS theme change. */
  fireChange: () => void
}

export function installMatchMediaStub(initialDark = false): MatchMediaStub {
  let matches = initialDark
  const listeners = new Set<Listener>()
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (query: string) => ({
      get matches() { return query === '(prefers-color-scheme: dark)' ? matches : false },
      addEventListener: (_event: string, listener: Listener) => { listeners.add(listener) },
      removeEventListener: (_event: string, listener: Listener) => { listeners.delete(listener) },
    }),
  })
  return {
    get matches() { return matches },
    flipMatches: (dark: boolean) => { matches = dark },
    fireChange: () => { for (const l of [...listeners]) l() },
  }
}
