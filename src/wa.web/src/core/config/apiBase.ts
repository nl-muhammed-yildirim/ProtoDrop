/**
 * CHORE-deploy-hardening (TA-8.2, TA-1.4): the single place the SPA learns where the API lives.
 * The base is injected at build time through `VITE_WA_API_BASE` (the Dockerfile ARG); staging passes the same-origin
 * `/api/v1`. Blank or missing keeps the local default so dev, vitest and e2e are unchanged.
 */

/** Local API base used when `VITE_WA_API_BASE` is blank or missing (fixed local port, no proxy). */
export const DEFAULT_API_BASE = 'http://localhost:8080/api/v1'

/**
 * Normalises a raw `VITE_WA_API_BASE` value.
 * @param raw The raw build-time value, possibly undefined or whitespace.
 * @returns The API base without a trailing slash, or {@link DEFAULT_API_BASE} when blank.
 */
export function resolveApiBase(raw: string | undefined): string {
  const trimmed = (raw ?? '').trim()
  if (trimmed === '') return DEFAULT_API_BASE
  return trimmed.replace(/\/+$/, '')
}

/** The resolved API base every feature prefixes its endpoints with. */
export const API_BASE = resolveApiBase(import.meta.env.VITE_WA_API_BASE)
