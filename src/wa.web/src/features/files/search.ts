// US-045-01 (FR-045-1, EC-045-3, UI notes): My Files search helpers — pure, unit-tested.
export const SEARCH_DEBOUNCE_MS = 300
/** Mirrors the server's SearchTerm.MaxLength — the UI trims before submit so Q_TOO_LONG is a server-side backstop only. */
export const SEARCH_MAX_LENGTH = 100

export function normalizeQuery(raw: string): string {
  return raw.trim().slice(0, SEARCH_MAX_LENGTH)
}

export function buildListUrl(base: string, status: string, q: string, cursor: string | null): string {
  let url = `${base}/transfers?status=${status}`
  if (q !== '') url += `&q=${encodeURIComponent(q)}`
  if (cursor !== null) url += `&cursor=${encodeURIComponent(cursor)}`
  return url
}
