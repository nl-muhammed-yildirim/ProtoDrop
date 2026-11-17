# T-008-07 — Profile screen UI: name, theme, plan + avatar menu (UI-Reference §5.5)

**Task:** `T-008-07` | **Spec row:** FR-008-4, AC-008-3 (client half), TA-4.2#12–13
**Story:** US-008-04-profile
**Milestone:** T-021 part 7 (profile screen UI; account deletion re-homes in T-008-09)
**Branch:** `feature/F-TRF-008`
**Size:** M

---

## DESIGN OVERVIEW

Pure `wa.web` task: the Profile screen (UI-Reference §5.5 profile card) plus the three top-bar wirings that T-008-03 deliberately left as placeholders. The server half already ships in T-008-06 (commit `d6f0dc6`): `GET /api/v1/auth/me`, `PATCH /api/v1/auth/me { displayName?, theme? }`, and `POST /api/v1/auth/logout` all exist — this task is a pure consumer. **No `.NET` diff** unless a reviewer proves a wire gap (cite the exact line). No new npm packages; no i18n files (AuthPage's inline-English precedent — grep-verified: zero i18n/locale/`useTranslation` hits in `wa.web/src`, so UI-Reference §7 "all strings from i18n files" is a P0-deferral carried by every prior screen; the keys `{area}.{screen}.{control}` are still respected as the eventual key namespace, noted in comments where relevant).

Three load-bearing new surfaces:
1. **`src/wa.web/src/features/profile/ProfilePage.tsx`** — display-name input + Save (`PATCH { displayName }`), theme select (system/light/dark, `PATCH { theme }`), read-only "Free" plan line, divider, and the reserved danger-zone slot for T-008-09 (D-2).
2. **The first signed-in client fetch** in the codebase — `/auth/me` needs `{ credentials: 'include' }` to carry the HttpOnly `wa.session` cookie cross-origin (`localhost:5173` → `localhost:8080`). Guest endpoints (AuthPage's signup/login/magic-link) never sent it. The CORS middleware (`src/wa.api/Middleware/CorsAllowListMiddleware.cs`, verified) already sets `Access-Control-Allow-Credentials: true` (comment: "httpOnly session cookie (TA-4.1.2) needs credentials") and echoes configured origins from `Wa:Cors:AllowedOrigins` — local config allows `http://localhost:5173` (`src/wa.api/Properties/launchSettings.json`) — **no .NET diff**.
3. **Pre-paint theme** (F-TRF-015): a tiny inline `<script>` in `index.html` reads the persisted profile from localStorage and applies `[data-theme]` before React paints, so dark survives sign-out/sign-in with no flash of wrong theme. Today the only theme mechanism is `@media (prefers-color-scheme: dark)` — grep-verified zero JS theming (`data-theme`/`documentElement.setAttribute`/`matchMedia`) anywhere in `wa.web`.

TopBar keeps its "fires callbacks only" contract (UnsubscribePage handleGo pattern): App owns route + session mutation. The avatar-menu **theme select** gets live-wired here too — its own comment says so (`Theme selection ships in T-008-07`), and it shares the same `PATCH /auth/me { theme }` call as the profile screen (D-4).

---

### D-1: Route module — new `src/wa.web/src/features/profile/profileRoute.ts`, pure predicate like `authRoute.ts`

Keep `matchAccountPath` a pure predicate (its doc comment pins the convention: "App reads window.location.pathname and passes a boolean by prop"). The profile screen owns the **exact** path `/account/profile`; AuthPage keeps catching all other `/account*`.

```ts
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
```

**App routing change (D-6).** Today, verbatim from `App.tsx`:

```tsx
import { matchAccountPath } from './features/auth/authRoute'
// ...
      <TopBar
        profile={session}
        onNavigateAccount={() => {
          window.history.pushState('', '', '/account')
          window.dispatchEvent(new PopStateEvent('popstate'))
        }}
        onSignOut={() => {
          clearSession()
          setSession(null)
        }}
      />
      <main>
        {unsubscribeToken !== null ? (
          <UnsubscribePage token={unsubscribeToken} />
        ) : matchAccountPath(pathname) ? (
          // Catch-all until T-008-07 takes over the section; keep matchAccountPath a pure predicate.
          <AuthPage onSignedIn={(p) => { setSession(p); goHome() }} />
        ) : linkId !== null ? (
```

becomes:

```tsx
import { matchAccountPath } from './features/auth/authRoute'
import ProfilePage from './features/profile/ProfilePage'
import { matchProfilePath, PROFILE_PATH } from './features/profile/profileRoute'
// ...
      <TopBar
        profile={session}
        onNavigateAccount={() => {
          window.history.pushState('', '', session ? PROFILE_PATH : '/account')
          window.dispatchEvent(new PopStateEvent('popstate'))
        }}
        onThemeChange={(theme) => handleThemeChange(theme)}
        onSignOut={() => handleSignOut()}
      />
      <main>
        {unsubscribeToken !== null ? (
          <UnsubscribePage token={unsubscribeToken} />
        ) : session && matchProfilePath(pathname) ? (
          <ProfilePage profile={session} onSignedIn={setSession} onThemeChange={handleThemeChange} onSignOut={handleSignOut} />
        ) : matchAccountPath(pathname) ? (
          // Signed-out guest keeps the sign-in card; /account/profile with no session falls
          // through here too (it still matches ACCOUNT_PATH_RE — order above is what wins).
          <AuthPage onSignedIn={(p) => { setSession(p); goHome() }} />
        ) : linkId !== null ? (
```

Notes:
- `session && matchProfilePath(...)` comes **before** `matchAccountPath` because the profile route is a subset of the account catch-all; keep `matchAccountPath` untouched and pure.
- Guest clicking "Profile" while signed out → falls through to AuthPage (sign-in card). No redirect logic in this task; that's the existing auth-catch-all behavior.
- TopBar's Profile row stays as-is visually but gains an `onClick={() => onNavigateAccount()}` — App decides `/account` vs `/account/profile` from session state, so **no new prop shape for navigation is needed**; only the two handlers below (D-4).
- After sign-in via AuthPage, `onSignedIn` already does `{ setSession(p); goHome() }` — the profile page then reads `profile` from App's session state, no extra fetch.

---

### D-2: Profile screen — new `src/wa.web/src/features/profile/ProfilePage.tsx`

Component contract (mirrors AuthPage's inline-fetch pattern and UnsubscribePage's handleGo navigation):

```tsx
import { useState } from 'react'

import type { StoredProfile } from '../../core/auth/session'
import { writeSession } from '../../core/auth/session'

// T-008-06 wire (TA-4.2#12–13), verified against src/wa.api/Program.cs — GET/PATCH /auth/me + logout:
const ME_BASE = 'http://localhost:8080/api/v1/auth/me'   // fixed-port absolute, AuthPage API_BASE precedent

interface ProfilePageProps {
  profile: StoredProfile                     // App owns the session; this is a prop (UnsubscribePage pattern)
  onSignedIn: (p: StoredProfile) => void      // App re-sets session state after a successful PATCH
  onThemeChange: (theme: 'system' | 'light' | 'dark') => void   // shared with TopBar — single source of truth in App
  onSignOut: () => void                      // same handler as TopBar's Sign out row
}

export function ProfilePage({ profile, onSignedIn, onThemeChange, onSignOut }: ProfilePageProps) {
  const [name, setName] = useState(profile.displayName)
  const [theme, setThemeLocal] = useState<'system' | 'light' | 'dark'>(profile.theme)
  const [savingName, setSavingName] = useState(false)
  const [savingTheme, setSavingTheme] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // PATCH { displayName } only — theme is a separate field so the two Save paths stay independent.
  async function saveName() { ... }          // see below
  function changeTheme(next: 'system' | 'light' | 'dark') { ... }   // PATCH { theme } + apply pre-paint attr

  return (
    <div className="pf">
      <h1 className="pf-heading">Profile</h1>
      {/* Display name */}
      <label className="pf-label" htmlFor="pf-name">Display name</label>
      <input id="pf-name" className="pf-input" type="text" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
      <button type="button" className="pf-save-btn" disabled={savingName || name.trim() === profile.displayName} onClick={() => void saveName()}>
        Save
      </button>
      {/* Theme select — same three options as TopBar; value from the account (server-wins, F-TRF-015). */}
      <label className="pf-label" htmlFor="pf-theme">Theme</label>
      <select id="pf-theme" className="pf-theme-select" value={theme} disabled={savingTheme} onChange={(e) => changeTheme(e.target.value as 'system' | 'light' | 'dark')}>
        <option value="system">System</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
      {/* Plan line — read-only in MVP (FR-008-4: "Free by default"). planCode comes from the session DTO; no extra fetch. */}
      <p className="pf-plan">{profile.planCode === 'free' ? 'Free' : profile.planCode}</p>
      {/* Danger zone divider + slot — US-008-06's Delete account button ships in T-008-09; reserve the slot, NOT the confirm modal. */}
      <hr className="pf-divider" />
      <div className="pf-danger-zone" aria-label="Danger zone">
        {/* T-008-09: delete-account button + GDPR confirm modal land here */}
      </div>
      {error && <p role="alert" className="pf-error">{error}</p>}
    </div>
  )
}
export default ProfilePage
```

**`saveName()` verbatim:**

```ts
  async function saveName() {
    setSavingName(true)
    setError(null)
    try {
      const res = await fetch(ME_BASE, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        // First signed-in client call — HttpOnly wa.session cookie must be sent cross-origin.
        credentials: 'include',
        body: JSON.stringify({ displayName: name.trim() })   // server trims + falls back to email local part on empty (T-008-06)
      })
      if (!res.ok) throw new Error((await res.json().catch(() => null))?.message ?? 'Could not save your display name.')
      const updated = (await res.json()) as Omit<StoredProfile, never>
      // Server already fell back for empty — show the SAVED value, never "(blank)" (exit check d).
      setName(updated.displayName)
      onSignedIn({ ...profile, displayName: updated.displayName })   // App re-sets session state; TopBar initial updates too.
    } catch {
      setError('Could not save your display name.')
    } finally {
      setSavingName(false)
    }
  }
```

**`changeTheme()` verbatim:**

```ts
  function changeTheme(next: 'system' | 'light' | 'dark') {
    // Apply the attribute pre-paint-style immediately so there's no flicker, then persist to the account.
    // F-TRF-015: guests fall back to prefers-color-scheme; a signed-in user's stored theme wins.
    if (next === 'system') document.documentElement.removeAttribute('data-theme')
    else document.documentElement.setAttribute('data-theme', next)

    setThemeLocal(next)
    onThemeChange(next)   // App updates its session state so TopBar's select stays in sync (D-4).

    setSavingTheme(true)
    setError(null)
    fetch(ME_BASE, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ theme: next })   // partial update — displayName untouched (T-008-06)
    })
      .then(async (res) => {
        if (!res.ok) throw new Error((await res.json().catch(() => null))?.message ?? 'Could not save your theme.')
        const updated = (await res.json()) as Omit<StoredProfile, never>
        onSignedIn({ ...profile, theme: updated.theme })   // server-wins; round-trip the stored value.
      })
      .catch(() => setError('Could not save your theme.'))
      .finally(() => setSavingTheme(false))
  }
```

**SSR guard (App.test.tsx renders via `renderToStaticMarkup` in node).** ProfilePage only mounts when `session !== null`, which only happens after a real browser sign-in — so it never runs under the SSR test. The only direct window access is inside `changeTheme()`/event handlers, not render. **No typeof-window guards needed** at component level; the App-level guard (`useState(() => readSession())` already has them in session.ts) is what keeps SSR hermetic. Do NOT add a top-level `window.` reference.

---

### D-3: Sign-out — wire `POST /auth/logout` (T-008-06 endpoint 12) from App's shared handler

Today, verbatim from `App.tsx`:

```tsx
        onSignOut={() => {
          clearSession()
          setSession(null)
        }
```

The spec says: "Sign out in the top bar avatar menu → POST /auth/logout → top bar returns to Sign in." The server clears the HttpOnly `wa.session` cookie (200 no-body); the client must then drop its local session so the TopBar flips to the guest branch. **New shared handler in App.tsx** (used by both TopBar and ProfilePage via D-6 wiring):

```tsx
  const handleSignOut = () => {
    // T-008-07: POST /auth/logout clears the server-side wa.session cookie (T-008-06, TA-4.2#13).
    // Fire-and-forget with a local fallback — even if the network fails we still clear the client session
    // so the TopBar returns to "Sign in" (exit check c); the stale server-side cookie will 401 on next GET /auth/me.
    fetch('http://localhost:8080/api/v1/auth/logout', { method: 'POST', credentials: 'include' }).catch(() => {})
    clearSession()
    setSession(null)
    goHome()
  }

  const handleThemeChange = (theme: 'system' | 'light' | 'dark') => {
    // Single source of truth for theme across TopBar + ProfilePage. The PATCH itself is owned by the
    // component that initiated it (ProfilePage.changeTheme or — in this task's scope — nothing else,
    // because TopBar's select is wired to the same onThemeChange but its own PATCH lives in D-4).
    setSession((s) => (s ? { ...s, theme } : s))
  }
```

**Why `goHome()` after sign-out:** the spec doesn't say where a signed-out user lands after clicking "Sign out" from the top bar. Falling to `/` (the landing drop zone) is the calm default and matches UnsubscribePage's handleGo pattern (single navigation, no redirects). The profile page's own "Sign out" row also calls `onSignOut` → same path.

**Exit check (b) — theme survives sign-out/sign-in from the account:** on sign-in, AuthPage's existing flow does `setSession(p)` where `p` is the `GET /auth/me` result (server-wins). The pre-paint script (D-5) then reads `profile.theme` from localStorage **before** React paints — so a fresh load after sign-out/sign-in applies dark before first paint, no flash. The client does NOT re-persist theme locally on its own; it's read from the stored session (which mirrors the server DTO exactly).

---

### D-4: TopBar theme select — enable + wire (D-6 adds `onThemeChange` prop)

Today, verbatim from `TopBar.tsx`:

```tsx
              <select className="tb-theme-select" defaultValue={profile.theme} disabled aria-label="Theme" title="Theme selection ships in T-008-07">
                <option value="system">System</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </select>
```

becomes (TopBar keeps firing callbacks only — App owns the PATCH via a new prop; do NOT duplicate the fetch in TopBar):

```tsx
              <select className="tb-theme-select" value={profile.theme} disabled={themeSaving} aria-label="Theme" onChange={(e) => onThemeChange(e.target.value as 'system' | 'light' | 'dark')} onClick={() => setMenuOpen(false)}>
                <option value="system">System</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </select>
```

**TopBarProps gains:** `onThemeChange: (t: 'system' | 'light' | 'dark') => void` and optionally `themeSaving?: boolean` (for the disabled state during PATCH — pass from App if you want; leave out for MVP scope since the select is inside a dropdown that closes on change).

**Where does TopBar's theme PATCH live?** To keep "TopBar fires callbacks only," App owns it: the same `handleThemeChange` in D-3 also issues the fetch. But then ProfilePage.changeTheme and App.handleThemeChange would both fire — to avoid double-PATCH, **ProfilePage owns its own PATCH** (D-2 verbatim) and calls a *local* state update + a separate App callback for session-sync only. Cleanest split:

- `App.handleThemeChange` = sets React session state **and** fires the PATCH (TopBar path).
- `ProfilePage.changeTheme` = fires the PATCH itself, then calls `onSignedIn({...profile, theme})` to sync App's state — and does NOT call a separate "fire fetch" callback.

To prevent both paths from running in one interaction, **TopBar's select only appears in the dropdown** (closes on change via `onClick={() => setMenuOpen(false)}`), and ProfilePage is a different screen. They can't be triggered simultaneously in this task — so no shared "who fired" flag needed. If a reviewer objects to the duplicated fetch code, hoist it into a `core/auth/useThemePatch.ts` hook in T-008-09 (out of scope here).

**TopBar Profile row:** gains `onClick={() => onNavigateAccount()}` — App's handler already picks `/account/profile` when signed in (D-6), so no new navigation prop.

---

### D-5: Pre-paint theme script — inline `<script>` in `index.html` (F-TRF-015)

Today, verbatim from `src/wa.web/index.html`:

```html
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>wa.web</title>
  </head>
```

Add (before `</head>`, so it runs before body paints — F-TRF-015 pre-paint rule):

```html
    <!-- T-008-07 (F-TRF-015): apply the signed-in user's stored theme before React paints, so dark
         survives sign-out/sign-in with no flash of wrong theme. Guests (no profile) get no attribute →
         CSS falls back to @media (prefers-color-scheme: dark). Server value wins — this reads the
         session mirror written by App on sign-in, which mirrors GET /auth/me exactly. -->
    <script>
      try {
        var p = JSON.parse(localStorage.getItem('protoDrop.session') || 'null'); // SESSION_KEY from core/auth/session.ts — NOT the cookie name (wa.session is HttpOnly)
        if (p && typeof p.theme === 'string' && p.theme !== 'system') {
          document.documentElement.setAttribute('data-theme', p.theme);
        } else {
          document.documentElement.removeAttribute('data-theme');
        }
      } catch (e) {}
    </script>
```

**CSS change to make `[data-theme]` win over the media query.** The existing `@media (prefers-color-scheme: dark)` block stays as-is for guests. Add a higher-specificity override so an explicit stored theme beats the OS setting in both directions:

Add to `src/wa.web/src/index.css`, immediately after the existing `@media (prefers-color-scheme: dark) { :root { ... } }` block (ends around line 45):

```css
/* T-008-07 (F-TRF-015): explicit stored theme wins over OS prefers-color-scheme in both directions.
   The pre-paint script in index.html sets [data-theme] from the signed-in profile; guests have no
   attribute → they stay on the @media (prefers-color-scheme: dark) fallback above. */
html[data-theme='dark'] :root, html[data-theme='dark'] {
  --bg: #0F1115;
  --bg-subtle: #171A21;
  --bg-elevated: #1C2028;
  --fg: #E8EAED;
  --fg-muted: #8A94A3;
  --fg-faint: #667080; /* T-008-03: missing UI-Reference §1 token */
  --accent: #5A9BF7;
  --accent-hover: #74ACF9;
  --accent-soft: #1B2A44;
  --border: #2A2F3A;
  --danger: #E3645C;
  --danger-soft: #2B1817; /* T-008-03: missing UI-Reference §1 token */
}

html[data-theme='light'] :root, html[data-theme='light'] {
  --bg: #FFFFFF;
  --bg-subtle: #F5F6F8;
  --bg-elevated: #FFFFFF;
  --fg: #101418;
  --fg-muted: #5F6B7A;
  --fg-faint: #93A0AF; /* T-008-03: missing UI-Reference §1 token */
  --accent: #4185F4;
  --accent-hover: #2F6FD8;
  --accent-soft: #EAF1FD;
  --border: #DDE2E8;
  --danger: #D6453D;
  --danger-soft: #FBEBEA; /* T-008-03: missing UI-Reference §1 token */
}
```

**Why duplicate the token values instead of reusing the media query?** CSS has no "group" you can re-apply outside a media block without `@supports` or nesting. The two blocks are the light and dark palettes from UI-Reference §1 verbatim; keeping them as separate, explicit `[data-theme]` rules makes the override obvious to a reviewer and avoids fragile selector tricks. (A P2 refactor could consolidate via CSS custom-property inheritance on `<html data-theme>` — out of scope.)

**Exit check (b) "no flash":** the script runs in `<head>` before body paints, before React mounts. `localStorage` read is synchronous. If the profile has `theme: 'dark'`, `[data-theme='dark']` is set on first paint → dark palette immediately. No JS framework involved; no async fetch (the session mirror is already in localStorage from sign-in — written by App via `writeSession`).

---

### D-6: Profile page CSS — new `.pf-*` block in `src/wa.web/src/index.css`

Follows the existing convention exactly (`.au-*` for auth, `.tb-*` for top bar, `.rp-*` for recipient, `.ls-*` for link screen). **Prefix:** `.pf-`. **Placement:** after the existing `.au-*` block (after line 736), before the shared focus-visible rule.

```css
/* T-008-07 (UI-Reference §5.5): profile screen — mirrors .au-card */
.pf {
  width: 100%;
  max-width: 560px;
}

.pf-heading {
  margin: 0 0 16px;
  font-size: var(--fs-h1); /* mirrors .au-heading — there is no --fs-h2 token in index.css */
  line-height: 1.3;
  color: var(--fg);
  text-align: left;
}

.pf-label {
  display: block;
  margin-bottom: 4px;
  font-size: var(--fs-small);
  color: var(--fg-muted);
  text-align: left;
}

.pf-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  font-size: var(--fs-small);
  background: var(--bg-subtle);
  color: var(--fg);
}

.pf-save-btn {
  min-height: 44px; /* F-TRF-017-4 touch targets */
  margin-top: 8px;
  padding: 12px 24px;
  background: var(--accent);
  color: #FFFFFF;
  border: none;
  border-radius: var(--radius-sm);
  font-size: var(--fs-small);
  cursor: pointer;
}

.pf-save-btn:hover {
  background: var(--accent-hover);
}

.pf-save-btn:disabled {
  opacity: 0.55;
  cursor: default;
}

.pf-theme-select {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  font-size: var(--fs-small);
  background: var(--bg-subtle);
  color: var(--fg);
}

.pf-plan {
  margin: 0;
  padding: 8px 0;
  font-size: var(--fs-small);
  color: var(--fg-muted);
}

/* Danger-zone divider + slot — US-008-06's delete button ships in T-008-09 (UI-Reference §5.5). */
.pf-divider {
  border: none;
  height: 1px;
  background: var(--border);
  margin: 16px 0;
}

.pf-danger-zone {
  padding: 8px 0;
  min-height: 24px; /* reserved slot — empty in T-008-07, button lands in T-008-09 */
}

.pf-error {
  margin: 0;
  padding: 8px 12px;
  background: var(--danger-soft);
  border-radius: var(--radius-sm);
  color: var(--danger);
  font-size: var(--fs-small);
  text-align: left;
}

/* Focus-visible on every focusable pf- element (joins the existing .tb/.au rule at line ~730). */
.pf button:focus-visible,
.pf select:focus-visible,
.pf input:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}
```

**Add `.pf` to the shared focus-visible rule at line ~730 as well**, or use the dedicated block above (shown). The existing rule is:

```css
.tb button:focus-visible,
.tb select:focus-visible,
.au button:focus-visible,
.au input:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}
```

Extend to include `.pf`:

```css
.tb button:focus-visible,
.tb select:focus-visible,
.au button:focus-visible,
.au input:focus-visible,
.pf button:focus-visible,
.pf select:focus-visible,
.pf input:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}
```

(Pick one approach — the dedicated block or the extension. The extension is fewer lines; use it.)

---

### D-7: Test file — new `src/wa.web/src/features/profile/ProfilePage.test.tsx`

Mirror `AuthPage.test.tsx` exactly (verified pattern): `jest.spyOn(global, 'fetch')`, hand-stub responses, 3–4 tests. No new npm packages. The existing AuthPage test has 7 tests with `stubFetch` and `setNativeValue` helpers — replicate that helper shape.

Tests to include:
1. **Save displayName happy path:** render ProfilePage with a signed-in profile; type "Ada Lovelace" into the display name input (use `setNativeValue` helper for React controlled inputs); click Save; assert fetch called once with `{ method: 'PATCH', body: JSON.stringify({ displayName: 'Ada Lovelace' }), credentials: 'include' }`; assert `onSignedIn` called with the updated profile.
2. **Save empty displayName → server fallback:** stub response returns `{ ...profile, displayName: 'ada' }` (email local part); after save, the input value is "ada" (the saved value, never blank).
3. **Theme change to dark:** select Dark; assert fetch called with `body: JSON.stringify({ theme: 'dark' })`; assert `onSignedIn` (or the App-level callback) called with updated theme; assert `document.documentElement.getAttribute('data-theme') === 'dark'`.
4. **Sign out:** call the sign-out handler (via a "Sign out" button in the profile page, or by invoking the prop); assert fetch called with `POST /auth/logout` and `credentials: 'include'`; assert `onSignedIn` (or App's session clear) called.

**SSR note:** ProfilePage.test.tsx runs under jsdom (not node-SSR like App.test.tsx), so `document.documentElement.setAttribute` is available without guards. The AuthPage test precedent confirms this.

---

### ERROR CASES

| Situation | Response | Code (closed set — verified member) |
|---|---|---|
| PATCH /auth/me: invalid theme value / displayName > 100 chars after trim / all-null body | 400 JSON problem body | `VALIDATION` (existing, T-008-06) |
| GET /auth/me on sign-in (AuthPage flow): no cookie / expired | 401 → AuthPage shows its existing error state | `UNAUTHENTICATED` (existing) |
| PATCH /auth/me: valid cookie but row missing/soft-deleted (user deleted account mid-session) | 404 JSON problem body | `NOT_FOUND` (existing, T-008-06) |
| POST /auth/logout: network failure | client catches; still clears local session → TopBar returns to "Sign in" (exit check c holds) | n/a — fire-and-forget with fallback |
| PATCH theme: server 400/401/404 | ProfilePage shows `role="alert"` error line; select reverts to the stored value from the session DTO | per-row above |

**No new ProblemCode member anywhere in this task.** `ProblemCodes.cs` is not in the file boundary. The profile screen consumes the existing T-008-06 wire shapes verbatim:
- `GET /auth/me` → `{ userId, email, displayName, theme, planCode }` (200) — already consumed by AuthPage on sign-in.
- `PATCH /auth/me { displayName? } | { theme? }` → updated DTO (200); server falls back empty displayName to email local part; theme must be exactly `system|light|dark`.
- `POST /auth/logout` → 200 no-body, clears `wa.session` cookie.

---

### FILES FOR CODER (exact commit boundary)

| File | Action | What to change |
|---|---|---|
| `src/wa.web/src/features/profile/profileRoute.ts` | CREATE | D-1 verbatim: `PROFILE_PATH` constant + `matchProfilePath` pure predicate. |
| `src/wa.web/src/features/profile/ProfilePage.tsx` | CREATE | D-2 verbatim: display name input + Save, theme select, read-only "Free" plan line, divider, reserved danger-zone slot. First signed-in client fetch (`credentials: 'include'`). SSR-safe (no top-level window). |
| `src/wa.web/src/features/profile/ProfilePage.test.tsx` | CREATE | D-7: 4 tests (save name happy, empty fallback, theme change, sign out). Mirror AuthPage.test.tsx fetch-stub convention. |
| `src/wa.web/src/App.tsx` | MODIFY | D-3 + D-6 verbatim: add `handleSignOut` (POST /auth/logout + clearSession + goHome), `handleThemeChange` (setSession theme + PATCH for TopBar path); add `ProfilePage` render branch **before** AuthPage catch-all; extend TopBar props with `onThemeChange`; TopBar Profile row's navigation already handled by existing `onNavigateAccount` (no new prop). |
| `src/wa.web/src/core/TopBar.tsx` | MODIFY | D-4 verbatim: enable the theme select (`value`, `onChange`, remove `disabled` + placeholder title); add `onClick={() => setMenuOpen(false)}` to close dropdown on change; Profile row gains `onClick={() => onNavigateAccount()}`. Props gain `onThemeChange`. |
| `src/wa.web/src/index.css` | MODIFY | D-5 + D-6 verbatim: add `[data-theme='dark']` and `[data-theme='light']` override blocks after the existing `@media (prefers-color-scheme: dark)` block; add `.pf-*` profile page styles; extend the shared focus-visible rule to include `.pf`. |
| `src/wa.web/index.html` | MODIFY | D-5 verbatim: add inline pre-paint theme `<script>` in `<head>`, before `</head>`. Reads the `protoDrop.session` localStorage mirror (session.ts SESSION_KEY), sets/removes `[data-theme]` on `document.documentElement`. |

**Verified, NO diff:** `src/wa.web/src/core/auth/session.ts` — `StoredProfile` already matches the GET /auth/me wire shape exactly (`{ userId, email, displayName, theme, planCode }`) and read/write/clearSession are SSR-safe. **Do NOT add a field.** A reviewer may want a theme-union type (`'system' \| 'light' \| 'dark'` instead of `string`) — that's a follow-up; keep this task additive-zero on session.ts.

**Excluded from the commit:** no `.NET` files (pure consumer of T-008-06); no new npm packages; no i18n files (inline-English precedent); no migration. **wa.web only → npm gates apply.**

---

### GATES (Definition of Done)

```bash
cd src/wa.web && npm run lint        # expect 0 errors, 0 warnings (eslint clean)
cd src/wa.web && npm run test:run    # expect 13 files + 1 new = 14 files; 156 + 4 new = 160 tests passing, 0 failing
cd src/wa.web && npm run build       # expect tsc -b green + vite build green (no type errors)
```

**Exit check (manual, after gates pass):**
- [ ] Set display name to "Ada Lovelace" → saved; a new transfer's recipient page shows "From: Ada Lovelace" (AC-008-3 client half). *Note:* the "new transfer shows From: Ada Lovelace" half is server-side snapshot-at-send-time (FR-002-4) — this task guarantees the display name **persists** in the session + server; the LinkScreen `fromName` init from `senderName ?? ''` already consumes it once LinkScreen is mounted (T-008-09 wires that end-to-end).
- [ ] Switch theme to dark → sign out and back in → dark theme applied from the account (persisted, no flash). *Verified by:* pre-paint script reads `profile.theme` from localStorage before React paints; server value wins on sign-in.
- [ ] Sign out → top bar shows "Sign in" again. *Verified by:* `handleSignOut` clears local session → TopBar guest branch renders.
- [ ] Empty display name save → falls back to email local part (server-side, T-008-06); Unicode names ("Müller", "Zoë") round-trip without mangling (UTF-8 JSON body, no client-side encoding).

Do NOT run the Playwright e2e suite — it's not in this task's gate. Commit: exactly one `feat: T-008-07 ...` commit staging ONLY the 7 files above (session.ts stays untouched), individually.

---

### ADR (Architecture Decision Records) — none required
No new npm packages (react, react-dom, zustand already present). No new .NET dependencies. No i18n package (inline strings per AuthPage precedent). The only architectural choice is the **duplicated fetch code** between ProfilePage.changeTheme and App.handleThemeChange (D-4) — documented above; a reviewer may flag it as a P2 refactor candidate (hoist to `core/auth/useThemePatch.ts`), but it's not a blocker.
