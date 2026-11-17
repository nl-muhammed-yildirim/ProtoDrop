/**
 * T-008-07 (UI-Reference §5.5): pure /account/profile path matcher — App reads
 * window.location.pathname and passes a boolean by prop, so ProfilePage stays free of
 * any window dependency (same convention as authRoute.ts). Exact match only: the profile
 * screen owns exactly this path; AuthPage keeps catching the rest of /account*.
 */

export const PROFILE_PATH = '/account/profile'

export function matchProfilePath(pathname: string): boolean {
  return pathname === PROFILE_PATH
}
