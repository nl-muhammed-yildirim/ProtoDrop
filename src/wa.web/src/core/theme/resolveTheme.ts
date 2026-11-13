/**
 * T-015-01 (F-TRF-015): theme resolution order — account choice > OS preference > light default.
 * Pure function so the bootstrap's logic is unit-testable without document/localStorage; index.html
 * duplicates these three lines inline because it must run before module loading (plan D-2).
 */

export type ResolvedTheme = 'dark' | 'light'

/** Stored account theme from the /auth/me session mirror ('system'|'light'|'dark'), or undefined for guests/corrupt data. */
export function resolveTheme(stored: 'system' | 'light' | 'dark' | undefined, prefersDark: boolean): ResolvedTheme {
  if (stored === 'light' || stored === 'dark') return stored // EC-015-1: an explicit account choice wins over the OS in both directions
  return prefersDark ? 'dark' : 'light' // system/absent → OS preference; no dark match → light default
}
