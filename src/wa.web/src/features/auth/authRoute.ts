/**
 * T-008-03 (UI-Reference §5.5): pure /account path matcher — App reads window.location.pathname and
 * passes a boolean by prop, so AuthPage itself stays free of any window dependency (same convention as
 * RecipientPage's linkId prop). True for exact /account and any /account/* (catch-all until T-008-07 takes over the section).
 */

export const ACCOUNT_PATH_RE = /^\/account(\/.*)?$/

export function matchAccountPath(pathname: string): boolean {
  return ACCOUNT_PATH_RE.test(pathname)
}

/** CHORE-magic-link-redeem (FR-008-1, EC-008-3): emailed link path — {origin}/auth?ml={token}. */
export const MAGIC_LINK_PATH = '/auth'

/**
 * Returns the redeem token for `/auth?ml=<token>`, else null. `URLSearchParams` decodes `%2B`; a legacy
 * unencoded `+` arrives as a space, which base64 never contains — map it back so pre-deploy links still work.
 */
export function matchMagicLinkRedeem(pathname: string, search: string): string | null {
  if (pathname !== MAGIC_LINK_PATH) return null
  const raw = new URLSearchParams(search).get('ml')
  if (!raw) return null
  return raw.replace(/ /g, '+')
}
