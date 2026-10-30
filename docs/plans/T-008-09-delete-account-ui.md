# T-008-09 — Danger zone: delete account confirm modal + post-delete redirect (FR-008-8)

**Task:** `T-008-09` | **Spec row:** FR-008-8, AC-008 (delete half), EC-008-2, TA-4.2#13
**Story:** US-008-06-delete-account
**Milestone:** T-021 part 9 (danger zone; re-homing server-side already shipped in T-008-06)
**Branch:** `feature/F-TRF-008`
**Size:** S

---

## DESIGN OVERVIEW

Pure `wa.web` task: the profile screen's reserved danger-zone slot (`ProfilePage.tsx` lines 97–100 — `<hr className="pf-divider" />` + empty `<div className="pf-danger-zone" aria-label="Danger zone">`, T-008-07 verbatim) gains a red-tinted **Delete account** button (ghost-danger) and the GDPR confirm modal. The server half ships in T-008-06 (commit `d6f0dc6`): `DELETE /api/v1/auth/me` does soft-delete + re-home (`OwnerAppUserId = NULL`, `SenderName` intact) + `account_deleted` outbox event + clears the `wa.session` cookie, returning **200 JSON `{ "userId": "<guid-no-dashes>" }`** (verified `src/wa.api/Program.cs:596–614`). This task is a pure consumer — **no `.NET` diff** unless a reviewer proves a wire gap (cite the exact line). The CORS allow-list already includes `DELETE` (`CorsAllowListMiddleware.cs` line 39: `"GET, POST, PATCH, DELETE, OPTIONS"`) and credentials — no diff. No new npm packages; no i18n files (AuthPage inline-English precedent); no new `ProblemCode` member (401/404 codes are all T-008-06's existing closed set).

The modal copy is pinned by the task spec instructions §2 — render it **verbatim** except `{display name}` interpolates the profile prop at render time (D-3):

> "Your account and profile are deleted. Your active transfers keep working and show 'From: {display name}' without your email."

It names what survives (transfers + sender identity) and what doesn't (account, profile, email) — no vague "everything will be deleted" (exit check b). The F-TRF-008 UI notes' shorter phrase ("Your active transfers keep working as Anonymous.") is the §5.5 layout note; the task spec's exact string wins (it also matches US-008-06's happy path with "Ada Lovelace").

Two load-bearing decisions this plan must close:

1. **Request ordering (D-2):** unlike sign-out (`handleSignOut` is fire-and-forget because a failed logout still means "signed out locally"), DELETE **must be awaited before** session clear + redirect + toast. If the request fails (401 expired cookie, 404 already deleted, network), the account may still exist — showing "Your account was deleted." or clearing local state would lie. On failure the modal stays open with an inline `role="alert"` error; cancel closes without a request (exit check d holds either way).
2. **Toast after redirect (D-5):** there is no app-level toast infrastructure — the only precedent is DropZone's component-local `toast: string | null` state + 5 s timer (`DropZone.tsx` lines 49, 60–83, 134–138). But the delete flow ends in a **route change** (`/account/profile` → `/`), so any ProfilePage/DangerZone-local state dies with the unmount. Resolution: the toast string lives in **App state**, passed as an optional `toast?: string | null` prop into DropZone, which renders it via its *existing* `.dz-toast role="status"` pattern (a new sibling line — not by reusing DropZone's internal paste/skip state). App sets `deleteToast` right before `goHome()`, and a 5 s timer in App clears it. The landing page is the only post-delete surface, so the toast can never be stranded on an unrendered route.

The handler lives in **App.tsx** (new `handleDeleteAccount`, sibling of `handleSignOut`), because App owns route + session mutation — the exact TopBar "fires callbacks only" contract (UnsubscribePage handleGo pattern). ProfilePage passes it down as a prop; DangerZone is the component that renders button + modal.

---

### D-1: DangerZone component — new `src/wa.web/src/features/profile/DangerZone.tsx`

Self-contained: owns modal-open state and the DELETE fetch (it already has the `profile.displayName` it needs for both copy interpolation and nothing else); fires **one** callback (`onDeleteAccount`) when 2xx so App does session-clear + redirect + toast. This mirrors ProfilePage's own-fetch pattern (D-2 of T-008-07's plan) rather than hoisting the fetch into App — same reasoning as `ProfilePage.changeTheme`: the component that owns the interaction owns the request; App owns navigation/session only.

```tsx
import { useEffect, useState } from 'react'

import type { StoredProfile } from '../../core/auth/session'

// T-008-06 wire (TA-4.2#13), verified against src/wa.api/Program.cs — DELETE /auth/me returns
// 200 JSON { userId }; the re-home transaction is server-side (OwnerAppUserId = NULL, SenderName kept).
const ME_BASE = 'http://localhost:8080/api/v1/auth/me'   // fixed-port absolute, ProfilePage.tsx precedent

interface DangerZoneProps {
  profile: StoredProfile           // display name feeds the modal copy interpolation only
  onDeleteAccount: () => void      // App: clearSession + setSession(null) + toast + goHome (D-2/D-5)
}

/**
 * T-008-09 (FR-008-8, UI-Reference §5.5 danger zone): red-tinted Delete account button + GDPR
 * confirm modal in the profile's reserved slot. No request is sent until Confirm — Cancel just
 * closes (exit check d). On 2xx fires onDeleteAccount exactly once; on failure keeps the modal
 * open with an inline alert so the user can retry or cancel.
 */
export function DangerZone({ profile, onDeleteAccount }: DangerZoneProps) {
  const [open, setOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Escape closes the modal (= cancel; no request has been sent or is in flight on close-after-failure).
  useEffect(() => {
    if (!open) return
    function onKey(e: KeyboardEvent): void { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey) }
  }, [open])

  async function close(): Promise<void> {
    setOpen(false)
    setError(null)   // a fresh open must not show the previous failure
  }

  async function confirm() {
    setDeleting(true)
    setError(null)
    try {
      const res = await fetch(ME_BASE, { method: 'DELETE', credentials: 'include' })
      if (!res.ok) throw new Error('Could not delete your account. Please try again.')
      onDeleteAccount()   // App clears session + navigates to / — this component unmounts here; no state after.
    } catch {
      setError('Could not delete your account. Please try again.')
    } finally {
      setDeleting(false)  // only reachable when the modal is still mounted (the error path).
    }
  }

  return (
    <>
      <button type="button" className="pf-delete-btn" onClick={() => setOpen(true)}>
        Delete account
      </button>
      {open && (
        <div className="pf-modal-backdrop" onClick={close} aria-hidden="true">
          <div className="pf-modal" role="dialog" aria-modal="true" aria-labelledby="pf-delete-title" onClick={(e) => e.stopPropagation()}>
            <h2 className="pf-modal-title" id="pf-delete-title">Delete your account?</h2>
            {/* Pinned GDPR copy (task spec instructions §2) — {display name} interpolates profile.displayName. */}
            <p className="pf-modal-copy">
              Your account and profile are deleted. Your active transfers keep working and show 'From: {profile.displayName}' without your email.
            </p>
            {error && <p role="alert" className="pf-modal-error">{error}</p>}
            <div className="pf-modal-actions">
              <button type="button" className="pf-modal-cancel" disabled={deleting} onClick={() => void close()}>Cancel</button>
              <button type="button" className="pf-modal-confirm" autoFocus disabled={deleting} onClick={() => void confirm()} aria-busy={deleting}>
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

export default DangerZone
```

Notes:

- **SSR:** no top-level `window.` access — the keydown effect and handlers only run in a browser; the component only mounts when `session !== null` (same reasoning as T-008-07's D-2 SSR note). No typeof-window guards needed at this level.
- **Focus:** Confirm button gets static HTML `autoFocus` (focus moves into the dialog on open); Escape + backdrop click close. A full focus trap is P2 — there are only three focusables in the modal.
- **Backdrop click closes = cancel.** `e.stopPropagation()` on the card keeps clicks inside from bubbling to the backdrop. If `deleting` is true and the user bails via Cancel/Escape/backdrop, the request completes server-side anyway (2xx clears the cookie; the client stays signed in locally — acceptable: worst case they're signed into a deleted account until their next 401, same staleness class as T-008-07's sign-out comment).
- **`aria-busy` + `disabled={deleting}`** make double-confirm impossible (the second click is dead before the first response lands — no double DELETE).

---

### D-2: Request ordering — await DELETE, then clear + redirect + toast (App owns it)

New handler in `App.tsx`, verbatim placement immediately after `handleSignOut`:

```tsx
  const [deleteToast, setDeleteToast] = useState<string | null>(null)   // D-5: survives the ProfilePage unmount on redirect

  const handleDeleteAccount = () => {
    // T-008-09 (FR-008-8): DELETE already returned 2xx — account is gone server-side, cookie cleared by the API.
    // Order matters (unlike sign-out's fire-and-forget D-3 of T-008-07): the toast claims success, so it only
    // fires on a confirmed 2xx; local state then clears unconditionally — a failed server outcome is impossible here.
    clearSession()
    setSession(null)
    setDeleteToast('Your account was deleted.')
    goHome()   // pushState / + popstate dispatch (existing App helper) → ProfilePage unmounts, landing renders.
  }

  useEffect(() => {
    if (!deleteToast) return
    const t = window.setTimeout(() => setDeleteToast(null), 5000)   // DropZone toast precedent: 5 s auto-dismiss
    return () => window.clearTimeout(t)
  }, [deleteToast])
```

And in the ProfilePage render branch (line 77 today):

```tsx
          <ProfilePage profile={session} onSignedIn={setSession} onThemeChange={handleThemeChange} onDeleteAccount={handleDeleteAccount} />
```

**Why not fire-and-forget like sign out?** `handleSignOut` can lie less: even a failed POST /auth/logout still means "treat me as signed out locally" (stale cookie 401s on next GET). DELETE is different — a failed request means the account **may still exist** (401 expired cookie, 404 row missing/soft-deleted already, network drop), so clearing session + toasting "Your account was deleted." would be premature. DangerZone awaits and only fires `onDeleteAccount` on 2xx; at that point all three local mutations are unconditional. The exit check (c) trio — redirect to `/`, toast, top bar "Sign in" — is one synchronous block after the await: no user-visible race where TopBar still shows the avatar while the page shows the landing.

**`handleDeleteAccount` reads `profile.displayName`?** No — App doesn't need it; the copy interpolation is DangerZone's (D-1). App only clears state and navigates.

---

### D-3: GDPR copy — pinned string, `{display name}` interpolated at render time

Exit check b: "Modal copy names what survives and what doesn't — no vague 'everything will be deleted'." The exact string (task spec instructions §2 / US-008-06 happy path):

```
Your account and profile are deleted. Your active transfers keep working and show 'From: {display name}' without your email.
```

Rendered as one `<p>` (D-1) with `{profile.displayName}` substituted — the display name is already in App's session state from sign-in/`PATCH`, so no extra fetch. What survives, named: **active transfers** + **"From:" sender identity**. What doesn't, named: **account and profile**, **email**. Test 4 (D-6) asserts the literal rendered text against a fixed display name — that is what pins "verbatim" in CI.

The title row (`Delete your account?`) and button labels (`Cancel` / `Delete`) are UI chrome, not pinned copy.

---

### D-4: Danger-zone slot wiring + CSS

**ProfilePage.tsx:** the reserved comment at lines 97–100 becomes the component; `onDeleteAccount` is added to `ProfilePageProps` (App owns the handler — TopBar contract unchanged):

```tsx
interface ProfilePageProps {
  profile: StoredProfile
  onSignedIn: (p: StoredProfile) => void
  onThemeChange: (theme: 'system' | 'light' | 'dark') => void
  onDeleteAccount: () => void              // T-008-09: App.handleDeleteAccount — fires after the 2xx DELETE (D-2).
}

      {/* Danger zone divider + slot — US-008-06's Delete account button ships in T-008-09; reserve the slot, NOT the confirm modal. */}
      <hr className="pf-divider" />
      <div className="pf-danger-zone" aria-label="Danger zone">
        <DangerZone profile={profile} onDeleteAccount={onDeleteAccount} />
      </div>
```

**CSS:** new `.pf-modal-*` + `.pf-delete-btn` block in `src/wa.web/src/index.css`, appended after the existing `.pf-error` rule (line 865). Uses only existing tokens (`--danger`, `--danger-soft`, `--fg-muted`, `--border`, `--radius-sm`, `--fs-small`):

```css
/* T-008-09 (FR-008-8, UI-Reference §5.5 danger zone): ghost-danger button + confirm modal */
.pf-delete-btn {
  min-height: 44px; /* F-TRF-017-4 touch targets — mirrors .au-ghost-btn */
  padding: 12px 24px;
  background: var(--danger-soft);
  color: var(--danger);
  border: none;
  border-radius: var(--radius-sm);
  font-size: var(--fs-small);
  cursor: pointer;
}

.pf-delete-btn:hover {
  color: #FFFFFF;
  background: var(--danger);   /* red-tinted → solid on hover (ghost-danger) */
}

.pf-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(16, 20, 24, 0.5);
}

.pf-modal {
  width: 100%;
  max-width: 480px;
  padding: 24px;
  background: var(--bg-elevated);
  border-radius: var(--radius-sm);
}

.pf-modal-title {
  margin: 0 0 8px;
  font-size: var(--fs-h1); /* mirrors .pf-heading — no --fs-h2 token in index.css */
  line-height: 1.3;
  color: var(--fg);
  text-align: left;
}

.pf-modal-copy {
  margin: 0 0 16px;
  font-size: var(--fs-small);
  color: var(--fg-muted);
  text-align: left;
}

.pf-modal-error {
  margin: 0 0 16px;
  padding: 8px 12px;
  background: var(--danger-soft);
  border-radius: var(--radius-sm);
  color: var(--danger);
  font-size: var(--fs-small);
  text-align: left;
}

.pf-modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

/* Cancel = ghost (au-ghost-btn pattern); Confirm = danger solid. */
.pf-modal-cancel {
  min-height: 44px; /* F-TRF-017-4 touch targets — mirrors .au-ghost-btn */
  padding: 12px 24px;
  background: transparent;
  color: var(--fg-muted);
  border: none;
  border-radius: var(--radius-sm);
  font-size: var(--fs-small);
  cursor: pointer;
}

.pf-modal-cancel:hover:not(:disabled) {
  color: var(--fg);
}

.pf-modal-confirm {
  min-height: 44px; /* F-TRF-017-4 touch targets */
  padding: 12px 24px;
  background: var(--danger);
  color: #FFFFFF;
  border: none;
  border-radius: var(--radius-sm);
  font-size: var(--fs-small);
  cursor: pointer;
}

.pf-modal-confirm:hover:not(:disabled) {
  opacity: 0.9;
}

.pf-modal-cancel:disabled,
.pf-modal-confirm:disabled {
  opacity: 0.55;
  cursor: default;
}
```

Focus-visible on the new controls is already covered by the existing shared rule (`.pf button:focus-visible` — `index.css` lines 762–772). No change to `.pf-danger-zone` (the slot's `min-height: 24px` now holds a 44 px button; flex-none, no clip).

---

### D-5: Toast after redirect — App-owned state, DropZone renders it

The component-local precedent (`DropZone.tsx` lines 49–83) doesn't survive the route change by itself. Solution: **App owns the string + timer; DropZone owns the render.** DropZone gains one optional prop and one sibling line — its internal paste/skip toast state stays untouched (different origin, same visual):

```tsx
// DropZone.tsx
export function DropZone({ toast }: { toast?: string | null }) {   // T-008-09: app-level post-delete toast (App state survives the unmount)
  ...
      {toast && (
        <div className="dz-toast" role="status">
          {toast}
        </div>
      )}
```

In App's landing branch: `<DropZone toast={deleteToast} />` (line ~86). `deleteToast` is set in the same synchronous block as `clearSession()`/`goHome()` (D-2), so by the time the landing paints, DropZone already has it — no flicker, nothing stranded. The 5 s timer lives in App's effect (D-2 verbatim); after it fires, `toast={null}` and the line unmounts. `role="status"` matches DropZone's existing toasts (screen-reader announcement). No portal, no context, no new package — this is the smallest client-only surface that satisfies exit check c.

---

### D-6: Tests — 4 new tests appended to `src/wa.web/src/features/profile/ProfilePage.test.tsx`

Same file as T-008-07's tests (the danger zone renders inside ProfilePage; keeps the feature's test surface in one place). Reuse that file's existing helpers verbatim: `renderProfilePage`, `stubFetch` (hand-stubbed, capture closure), `installLocalStorageShim`/`clearSession`, and the signed-in-App render pattern from its 4th test (lines 174–211 — writes session via `writeSession(BASE_PROFILE)`, renders full `App`). One change: extend `renderProfilePage`'s signature with an optional `onDeleteAccount?: () => void` param (default no-op) — or render the full App for all four new tests; either is acceptable, full-App rendering preferred because 3 of 4 assertions live in App's tree (TopBar guest branch, landing toast).

Tests (naming follows the file's existing `it()` convention with AC/exit-check citations):

1. **Cancel → no request:** render signed-in App; open avatar dropdown is NOT needed — navigate directly: write session, `window.history.pushState` + popstate to `/account/profile`, open modal by clicking `.pf-delete-btn`; click the modal's Cancel button (`.pf-modal-cancel`). Assert: `fetch` never called (stub throws if invoked), modal unmounted (`host.innerHTML` has no `.pf-modal`), delete button still visible, session still in localStorage (`readSession()` non-null). Covers exit check d.
2. **Confirm happy path → DELETE 200 { userId } → guest landing + toast:** same setup; stub `fetch` to return `new Response(JSON.stringify({ userId: '1' }), { status: 200 })`; click Confirm (`.pf-modal-confirm`). Assert in one block after the awaited act: fetch called **exactly once** with `{ method: 'DELETE', credentials: 'include' }` against `/auth/me`; TopBar shows guest `<button ... class="tb-signin"` (session cleared — exit check c); landing toast present (`host.innerHTML` contains `Your account was deleted.` in a `role="status"` line); session gone from localStorage (`readSession() === null`). Covers exit checks a + c.
3. **Confirm → 401 UNAUTHENTICATED:** stub returns the T-008-06 problem shape (401, `UNAUTHENTICATED` title — any non-ok status works; use 404 NOT_FOUND to also cover the already-deleted row). Click Confirm. Assert: modal still open, `role="alert"` line shows "Could not delete your account. Please try again.", session **unchanged** (localStorage round-trips), no redirect (pathname still `/account/profile`, profile screen still in DOM — My Files/landing absent).
4. **GDPR copy pinned:** render with a fixed display name ("Ada Lovelace"); open modal; assert the `.pf-modal-copy` textContent equals the pinned string exactly: `Your account and profile are deleted. Your active transfers keep working and show 'From: Ada Lovelace' without your email.` — this is what locks exit check b against future copy edits.

All four run under jsdom (file already has `// @vitest-environment jsdom`); real timers suffice (no setTimeout assertions required — the toast auto-dismiss timer firing is not asserted; asserting it would need fake timers and the existing suite deliberately avoids them).

---

### ERROR CASES

| Situation | Response | Code (closed set — verified member) |
|---|---|---|
| DELETE /auth/me: no cookie / expired (session died while profile was open) | 401 JSON problem body → DangerZone keeps modal open, inline `role="alert"` error; session unchanged; user can retry or cancel | `UNAUTHENTICATED` (existing, T-008-06) |
| DELETE /auth/me: valid cookie but row missing/soft-deleted already (double delete via two tabs) | 404 JSON problem body → same inline error path as above; re-create gets a new user id either way (US-008-06 alternative flow) | `NOT_FOUND` (existing, T-008-06) |
| DELETE /auth/me: network failure (dev server down) | fetch throws → same inline error path; account may exist or not — the copy doesn't promise either way until 2xx (D-2 ordering is what makes this honest) | n/a — client catch |

**No new ProblemCode member anywhere in this task.** `ProblemCodes.cs` is not in the file boundary. The client consumes T-008-06's wire shapes verbatim:
- `DELETE /auth/me` → 200 JSON `{ userId }`, clears `wa.session` cookie (re-home transaction server-side — `OwnerAppUserId = NULL`, `SenderName` intact; this task never reads or writes transfer rows).
- Exit check a (3 active transfers survive as anonymous, recipient pages work) is **AC'd by T-008-06's integration test** against the same endpoint (`DELETE me → user soft-deleted, transfer re-homed`) — the client adds no new server behavior; no e2e gate here.

---

### FILES FOR CODER (exact commit boundary)

| File | Action | What to change |
|---|---|---|
| `src/wa.web/src/features/profile/DangerZone.tsx` | CREATE | D-1 verbatim: ghost-danger Delete account button + GDPR confirm modal (backdrop, Escape, Cancel/Confirm), awaiting DELETE /auth/me with `credentials: 'include'`; fires `onDeleteAccount` once on 2xx; inline alert on failure. SSR-safe (no top-level window). |
| `src/wa.web/src/features/profile/ProfilePage.tsx` | MODIFY | D-4 verbatim: add `onDeleteAccount: () => void` to props; replace the reserved comment inside `.pf-danger-zone` with `<DangerZone profile={profile} onDeleteAccount={onDeleteAccount} />`. Nothing else in this file moves. |
| `src/wa.web/src/App.tsx` | MODIFY | D-2 verbatim: add `deleteToast` state + 5 s timer effect, `handleDeleteAccount` (clearSession + setSession(null) + toast + goHome — called only after the 2xx), pass `onDeleteAccount={handleDeleteAccount}` to ProfilePage and `toast={deleteToast}` to DropZone. |
| `src/wa.web/src/features/landing/DropZone.tsx` | MODIFY | D-5 verbatim: add optional `toast?: string | null` prop; render it as a sibling `{toast && <div className="dz-toast" role="status">...}</div>` line beside the internal toast. Internal paste/skip state untouched. |
| `src/wa.web/src/index.css` | MODIFY | D-4 verbatim: append `.pf-delete-btn`, `.pf-modal-backdrop/-modal/-title/-copy/-error/-actions/-cancel/-confirm` block after the `.pf-error` rule (line 865). Existing tokens only; focus-visible already covered. |
| `src/wa.web/src/features/profile/ProfilePage.test.tsx` | MODIFY | D-6: append 4 tests (cancel → no request; confirm happy path → guest landing + toast + session cleared; 401/404 error → modal stays open, session unchanged; pinned GDPR copy string). Reuse the file's existing `stubFetch`, localStorage shim, and full-App render pattern. |

**Verified, NO diff:** `.NET` (pure consumer of T-008-06 — Program.cs line 596 endpoint + CORS line 39 both verified live); `session.ts` (read/write/clear all exist and are SSR-safe — no new field); `TopBar.tsx` (no new prop — the delete flow never touches TopBar directly; it flips to "Sign in" because App's session became null); `App.test.tsx` (SSR render is guest-only, unaffected by the new optional DropZone prop). **6 files total (1 CREATE, 5 MODIFY).** No new npm packages; no i18n files; no migration. **wa.web only → npm gates apply.**

**Commit:** exactly one `feat: T-008-09 danger zone delete account confirm modal` commit, staging ONLY the six file paths above individually — never `git add -A` (repo carries unrelated untracked `NUL`, `docs/plans/T-008-03-auth-screen-ui.md`, and a modified `.codegraph/.gitignore`).

---

### GATES (Definition of Done)

```bash
cd src/wa.web && npm run lint        # expect 0 errors, 0 warnings (eslint clean)
cd src/wa.web && npm run test:run    # 14 files (no new file — tests append to ProfilePage.test.tsx); 160 + 4 new = 164 tests passing, 0 failing
cd src/wa.web && npm run build       # expect tsc -b green + vite build green (no type errors)
```

Baseline verified at plan time: **14 files / 160 tests** (one flaky failure observed on a single full-suite run — the TopBar sign-out test passed in isolation and on re-run; if it flakes again, rerun once before routing to Coder).

**Exit check (manual, after gates pass):**
- [ ] Delete flow with 3 active transfers → all 3 survive as anonymous (`OwnerAppUserId=NULL`, `SenderName` intact), recipient pages still work. *Verified by:* T-008-06's integration test against the same endpoint — this task adds no server behavior (exit check a).
- [ ] Modal copy names what survives and what doesn't — *verified by* D-6 test 4 (pinned string assertion) + the D-3 render.
- [ ] Confirm → redirect to `/` + toast "Your account was deleted."; top bar shows `Sign in`. My Files is empty (no owner). *Verified by:* D-2's single synchronous clear+redirect+toast block — My Files has no dedicated UI surface yet (T-021 ships TopBar/Profile only; T-022 depends on this feature), so "My Files is empty" holds vacuously client-side: with the owner's local session cleared there is no files list to fetch against, and server-side re-homing is T-008-06's AC.
- [ ] Cancel → no request, profile unchanged. *Verified by* D-6 test 1 (fetch-throws-if-invoked stub).

Do NOT run the Playwright e2e suite — not in this task's gate.

---

### ADR — none required
No new npm packages; no new .NET dependencies; no i18n package. The only architectural choice is **App-owned toast state** (D-5) instead of a generic app-level toast primitive — deliberate: one consumer, five lines, zero infrastructure; if a second post-navigation toast appears (e.g., My Files in T-022), hoist to `core/` then.
