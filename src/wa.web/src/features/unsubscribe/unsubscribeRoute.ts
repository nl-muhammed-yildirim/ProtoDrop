/**
 * T-006-03 (FR-006-5): pure /unsubscribe/{token} path matcher — App reads window.location.pathname and passes token by prop,
 * so UnsubscribePage itself stays free of any window dependency.
 */

export const UNSUBSCRIBE_PATH_RE = /^\/unsubscribe\/(.+)$/

export function matchUnsubscribePath(pathname: string): string | null {
  const m = pathname.match(UNSUBSCRIBE_PATH_RE)
  return m?.[1] ?? null
}
