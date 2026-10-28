/**
 * T-008-03 (UI-Reference §5.5): pure /account path matcher — App reads window.location.pathname and
 * passes a boolean by prop, so AuthPage itself stays free of any window dependency (same convention as
 * RecipientPage's linkId prop). True for exact /account and any /account/* (catch-all until T-008-07 takes over the section).
 */

export const ACCOUNT_PATH_RE = /^\/account(\/.*)?$/

export function matchAccountPath(pathname: string): boolean {
  return ACCOUNT_PATH_RE.test(pathname)
}
