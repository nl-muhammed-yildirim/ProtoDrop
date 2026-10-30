/**
 * T-008-03 (FR-008-3): client-side session persistence.
 *
 * Signup's wire shape carries a profile DTO + sessionToken but sets no cookie (T-008-01); login's
 * HttpOnly `wa.session` cookie is not readable by JS either way. So the client persists the DTO in
 * localStorage under 'protoDrop.session'. The typeof-window guard keeps readSession() SSR-safe —
 * App.test.tsx renders via renderToStaticMarkup (no window) and expects the guest TopBar.
 */

export type StoredProfile = {
  userId: string
  email: string
  displayName: string
  theme: 'system' | 'light' | 'dark'
  planCode: string
}

const SESSION_KEY = 'protoDrop.session'

export function readSession(): StoredProfile | null {
  if (typeof window === 'undefined') return null // SSR-safe (App.test.tsx)
  try {
    const raw = window.localStorage.getItem(SESSION_KEY)
    if (!raw) return null
    const parsed: unknown = JSON.parse(raw)
    // Defensive shape check — null on corrupt JSON (per plan).
    if (typeof parsed !== 'object' || parsed === null) return null
    const p = parsed as Record<string, unknown>
    if (typeof p.userId !== 'string' || typeof p.email !== 'string' || typeof p.displayName !== 'string' || typeof p.theme !== 'string' || typeof p.planCode !== 'string') {
      return null
    }
    return parsed as StoredProfile
  } catch {
    return null
  }
}

export function writeSession(profile: StoredProfile): void {
  if (typeof window === 'undefined') return // SSR-safe no-op
  try {
    window.localStorage.setItem(SESSION_KEY, JSON.stringify(profile))
  } catch {
    // localStorage can throw in private modes — session is still valid server-side via the cookie;
    // this is a best-effort persistence for the SPA's own routing state.
  }
}

export function clearSession(): void {
  if (typeof window === 'undefined') return
  try {
    window.localStorage.removeItem(SESSION_KEY)
  } catch {
    // Same as writeSession — no throw path.
  }
}
