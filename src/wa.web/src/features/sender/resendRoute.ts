/**
 * T-010-03 (FR-010-4): pure /resend/{transferId} path matcher — same pattern as filesRoute.ts / recipientRoute.ts.
 * App reads window.location.pathname and passes the original transfer's ID by prop, so ReSendScreen stays
 * free of any window dependency. The GUID is lowercase hex with dashes (standard .NET Guid.ToString() output
 * from endpoint 14's My Files rows) — uppercase never appears in a real transfer ID.
 */

export const RESEND_PATH_RE = /^\/resend\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/

export function matchResendPath(pathname: string): string | null {
  const m = pathname.match(RESEND_PATH_RE)
  return m?.[1] ?? null
}
