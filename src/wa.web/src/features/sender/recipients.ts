export const MAX_EMAILS = 20

/**
 * Splits raw recipient input on `[\r\n,;\s]+`, trims each token, drops empties,
 * lowercases, and dedupes while preserving first-seen order.
 */
export function parseRecipients(raw: string): string[] {
  const seen = new Set<string>()
  const result: string[] = []

  for (const token of raw.split(/[\r\n,;\s]+/)) {
    const trimmed = token.trim()
    if (trimmed.length === 0) continue
    const lower = trimmed.toLowerCase()
    if (seen.has(lower)) continue
    seen.add(lower)
    result.push(lower)
  }

  return result
}

/**
 * Basic client-side pre-flight check. The server's .NET MailAddress is the real rule.
 * Must contain exactly one `@`, a non-empty local part, and a domain with at least one dot.
 */
export function validateAddress(addr: string): boolean {
  const atCount = (addr.match(/@/g) ?? []).length
  if (atCount !== 1) return false

  const [local, domain] = addr.split('@')
  if (local.length === 0) return false
  if (domain.length === 0) return false
  if (!domain.includes('.')) return false

  return true
}