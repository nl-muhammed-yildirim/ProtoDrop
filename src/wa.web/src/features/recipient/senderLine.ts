/** Server default for an unnamed sender (F-TRF-002, SendTransfer.cs ResolveSenderName) — mirrored for blank wire values. */
export const ANONYMOUS_SENDER = 'Anonymous'

/** F-PRF-001 FR-021-2 / US-021-03: person first, org second; blank parts dropped, never a dangling separator. */
export function formatSenderLine(personName: string, orgName: string | null): string {
  const person = personName.trim()
  const org = orgName?.trim() ?? ''
  if (person !== '' && org !== '') return `${person} · ${org}`
  return person || org || ANONYMOUS_SENDER
}
