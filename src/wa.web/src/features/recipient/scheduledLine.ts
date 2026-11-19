import { formatDate } from '../../core/i18n/format'

/** F-PRF-002 (FR-022-6): true only when iso parses and is strictly after nowMs — a past due time shows no line. */
export function isPreDue(iso: string | null, nowMs: number): boolean {
  if (iso === null) return false
  const due = new Date(iso).getTime()
  return !Number.isNaN(due) && due > nowMs
}

/** EC-022-6: the UTC wire value rendered in the viewer's browser locale + time zone (zone label explicit). */
export function formatScheduledWhen(iso: string): string {
  return formatDate(iso, { hour: '2-digit', minute: '2-digit', timeZoneName: 'short' })
}
