/**
 * T-011-10 (UI-Reference §5.6): pure /admin/* path matcher — App reads
 * window.location.pathname and passes the matched sub-screen by prop, so AdminShell stays free of
 * any window dependency (same convention as filesRoute.ts). Exact match only: '/admin' alone → null;
 * the five admin screens own their exact paths. No window access — App.test.tsx must stay hermetic.
 */

export const ADMIN_OVERVIEW_PATH = '/admin/overview'
export const ADMIN_TRANSFERS_PATH = '/admin/transfers'
export const ADMIN_USERS_PATH = '/admin/users'
export const ADMIN_FLAGS_PATH = '/admin/flags'
export const ADMIN_SUPPRESSIONS_PATH = '/admin/suppressions'

export type AdminScreen = 'overview' | 'transfers' | 'users' | 'flags' | 'suppressions'

// §5.6 order — left nav renders these in this exact sequence.
export const ADMIN_NAV_ITEMS: { label: string; path: string }[] = [
  { label: 'Overview', path: ADMIN_OVERVIEW_PATH },
  { label: 'Transfers', path: ADMIN_TRANSFERS_PATH },
  { label: 'Users', path: ADMIN_USERS_PATH },
  { label: 'Flags', path: ADMIN_FLAGS_PATH },
  { label: 'Suppressions', path: ADMIN_SUPPRESSIONS_PATH },
]

export function matchAdminPath(pathname: string): AdminScreen | null {
  switch (pathname) {
    case ADMIN_OVERVIEW_PATH: return 'overview'
    case ADMIN_TRANSFERS_PATH: return 'transfers'
    case ADMIN_USERS_PATH: return 'users'
    case ADMIN_FLAGS_PATH: return 'flags'
    case ADMIN_SUPPRESSIONS_PATH: return 'suppressions'
    default: return null
  }
}
