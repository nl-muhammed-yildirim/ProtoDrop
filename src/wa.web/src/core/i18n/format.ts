/**
 * T-014-03 (FR-014-4 / EC-014-2): Intl formatters for dates/times using the resolved locale.
 * Uses i18n.language so dates/numbers are localized app-wide.
 */
import i18n from './init'

/**
 * Format a date using the current i18n locale.
 * Default options: { month: '2-digit', day: 'numeric', year: 'numeric' } — numeric
 * d.M.y so de → "23.08.2026" (AC-014-4). Callers needing the abbreviated form pass
 * { month: 'short' } explicitly (e.g., files/formatting.ts formatShortDate).
 */
export function formatDate(iso: string, opts?: Intl.DateTimeFormatOptions): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const defaultOpts: Intl.DateTimeFormatOptions = { month: '2-digit', day: 'numeric', year: 'numeric' }
  return new Intl.DateTimeFormat(i18n.language, { ...defaultOpts, ...opts }).format(d)
}

/**
 * Format a time using the current i18n locale.
 * Default options: { hour: '2-digit', minute: '2-digit' }
 */
export function formatTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return new Intl.DateTimeFormat(i18n.language, { hour: '2-digit', minute: '2-digit' }).format(d)
}
