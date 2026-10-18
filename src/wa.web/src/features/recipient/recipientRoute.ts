/**
 * T-003-02: pure /t/{linkId} path matcher — App reads window.location.pathname and passes linkId by prop,
 * so RecipientPage itself stays free of any window dependency (same convention as LinkScreen's linkId prop).
 *
 * The 8-char charset follows wa.domain/ValueObjects/LinkId.cs (authoritative over the task doc): Crockford
 * base32 minus i,l,o,u — digits + lowercase a-z. Uppercase never appears in a real link; accepting only the
 * real charset means malformed URLs fall through to the landing page instead of a bogus transfer fetch.
 */

export const RECIPIENT_PATH_RE = /^\/t\/([0-9a-hjkmnpqrswxyz]{8})$/

export function matchLinkPath(pathname: string): string | null {
  const m = pathname.match(RECIPIENT_PATH_RE)
  return m?.[1] ?? null
}
