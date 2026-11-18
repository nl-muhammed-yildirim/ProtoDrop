/**
 * US-020-03 (UI-Reference §5.7): pure /account/plan path matcher — same shape as profileRoute.ts.
 * Exact match only; App checks it before the /account* catch-all because the path also matches ACCOUNT_PATH_RE.
 */

export const PLAN_PATH = '/account/plan'

export function matchPlanPath(pathname: string): boolean {
  return pathname === PLAN_PATH
}
