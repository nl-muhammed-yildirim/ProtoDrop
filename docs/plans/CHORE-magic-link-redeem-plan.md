# CHORE-magic-link-redeem — Plan

**Issue:** #726 (Task) · **Branch:** `feature/CHORE-magic-link-redeem` (from develop a4f3eeb) · **Spec anchors:** FR-008-1/3, AC-008-2, EC-008-3 (`docs/features/Phase 0-MVP/F-TRF-008/F-TRF-008-accounts.md:49`), TA-9.1, TA-4.1.3, TA-8.2, TA-8.5, TA-10.2.

## 1. Goal

Clicking the magic-link / forgot-password email link (`{publicOrigin}/auth?ml=<token>`) signs the user in: the worker URL-encodes the base64 token, and src/wa.web gets an `/auth?ml=` route that redeems it once (StrictMode-safe), stores the session exactly like password login and goes home — 400/410 show the server's Problem+JSON copy. **Exit check:** new workers unit tests assert `%2B %2F %3D` in both CTA URLs; vitest covers matcher, success, 400, 410 and the StrictMode single-POST guard; full gate green.

## 2. Design & Decisions

- **D-1 Encode at the two CTA builders, inline.** `Uri.EscapeDataString(rawToken)` at `src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs:96` and `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs:99`. Two one-line call sites — no shared helper (would be a shallow module). The raw token sent to redeem is unchanged, so hashing (`RedeemMagicLinkHandler.cs:51`) and `Convert.FromBase64String` (`:47`) keep working (TA-9.1). No API / endpoint diff.
- **D-2 Forgot-password lands on the same redeem — confirmed.** `ForgotPasswordHandler.cs` persists an `AuthToken` row with `Purpose = 2` (const at `:30`, row at `:57-67`) and the same base64 raw token in the payload (`:74`); `RedeemMagicLinkHandler.Handle` looks up by `TokenHash` only (`RedeemMagicLinkHandler.cs:51-52`) and never filters on Purpose; the CTA comment at `SendForgotPasswordEmailHandler.cs:97` already states it. So one web route serves both emails.
- **D-3 Route shape (TA-8.2, existing pathname routing).** New pure matcher `matchMagicLinkRedeem(pathname, search): string | null` in `src/wa.web/src/features/auth/authRoute.ts` (sibling of `matchAccountPath`, `authRoute.ts:9`). Returns the token only for `pathname === '/auth'` with a non-empty `ml`; `/auth` without `ml` returns null and keeps falling through to the existing SPA 404 (`App.tsx:165-171`) — minimal, no new surface. `URLSearchParams.get('ml')` decodes `%2B`; it also turns a literal `+` into a space, so the matcher maps spaces back to `+` (base64 never contains spaces) — this keeps links mailed *before* this deploy (≤ 10 min TTL) working at zero cost.
- **D-4 App needs `search`.** App only tracks `pathname` (`App.tsx:44`, popstate at `:56`). Add a sibling `search` state initialised the same SSR-safe way and refreshed in the same `onPop`. The redeem branch goes **second**, right after the unsubscribe branch (`App.tsx:137-138`) and before the session-gated branches: an emailed link must redeem even when a (possibly different) session is already active.
- **D-5 Session storage = the password-login path, no duplicate logic.** AuthPage on 2xx does `writeSession(dto)` then `onSignedIn(dto)` (`AuthPage.tsx:104-106`); App's callback is `(p) => { setSession(p); goHome() }` (`App.tsx:154`). The redeem page calls the same `writeSession` from `core/auth/session.ts:43` and receives the identical App callback. The redeem response body has the same wire shape as login (`AuthEndpoints.cs:101-109` vs login), and the endpoint sets the same `wa.session` cookie (`:93-99`) — so the fetch must send `credentials: 'include'` (cross-origin :5173 → :8080, as `AuthPage.tsx:93`).
- **D-6 Errors reuse AuthPage's existing copy rule — no new i18n keys.** AuthPage renders non-2xx Problem+JSON `details` ?? `title` verbatim, fallback auth.error.generic (`AuthPage.tsx:110-121`). For redeem: 410 body is `ProblemWriter.Build(410, LINK_EXPIRED, TitleOf(...))` (`AuthEndpoints.cs:119`) → details "This link has expired" (`ProblemCodes.cs:107`) = the EC-008-3 copy; 400 VALIDATION details = "The provided magic link token is malformed." (`RedeemValidationException.cs:16`). EC-008-3's "Send a new link" button maps onto the existing key auth.ghost.magicLink ("Email me a magic link", `en.json:115`) which navigates to `/account` (the AuthPage with that ghost flow). `mapProblem` (`core/api/errorMap.ts:62`) is NOT used: it sends LINK_EXPIRED to the 500 screen (`errorMap.test.ts:56`), which would be wrong copy here. Pending state: an empty `au` card with `aria-busy="true"` and the existing auth.page.ariaLabel key — no new copy.
- **D-7 StrictMode / single-use guard.** `main.tsx:12` wraps the app in `<StrictMode>`; dev double-runs effects, and a second POST would 410 and overwrite the success. A module-level `Map<string, Promise<RedeemOutcome>>` in the page file memoises the request per token (`redeemOnce`); each effect run awaits the shared promise and an `active` flag set false in cleanup discards stale results. Result: exactly one POST and exactly one `onSignedIn` under StrictMode. Entries are never evicted (one per token per page load; a re-run on the same token must not re-POST). Not exported (react-refresh only-export-components).
- **D-8 Telemetry: none added.** Password login emits no client event (AuthPage has no `trackClientEvent`); the server already emits `login_success` (and `user_created` on create-on-redeem) in `RedeemMagicLinkHandler.cs:82,110` (TA-10.2). Mirroring that = no client call.
- **Golden rules:** no package, no schema, no env var, no new ProblemCode, no new telemetry name, no CSS token (reuse `.au`, `.au-card`, `.au-heading`, `.au-error`, `.au-ghost-btn` classes already in `index.css`) → theme/contrast scripts not required.

## 3. Implementation Plan

1. **Encode CTA tokens (D-1).** Edit the two `ctaUrl` lines; update both step-2 comments to `ml={Uri.EscapeDataString(rawToken)}`. Run `dotnet format src/wa.slnx`.
2. **Workers unit tests** — CREATE `tests/wa.workers.unit/Email/AuthEmailCtaUrlTests.cs` (wa.workers.unit is where the real `Emails/templates/**` are copied next to the test binary — see `EmailAudienceClassificationTests.cs:106-112`; the auth handlers load templates from `AppContext.BaseDirectory`, so they cannot run in wa.application.unit). Private stubs: `StubEmailSender` + `RecordingPublisher` copied from `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs:210-233`; telemetry `new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[] { 0 })` (precedent `ZipFunctionCorrelationTests.cs:219`); config via `ConfigurationBuilder().AddInMemoryCollection` with `Wa:Url:Public = https://app.example.com`; `NullLogger<T>.Instance`; `TimeProvider.System`. Token constant `"ab+cd/ef=="` (valid base64 not required — handler does not decode). Envelope: `Type = EventType.MagicLinkRequested`, payload `new { email = "u@example.com", token = Token, expiresAtUtc = DateTime.UtcNow.AddMinutes(10) }` (forgot-password adds `purpose = 2`). Facts:
   - `MagicLink_CtaUrl_IsUrlEncoded` — sent HtmlBody and TextBody contain `https://app.example.com/auth?ml=ab%2Bcd%2Fef%3D%3D` and do NOT contain `ml=ab+cd`.
   - `ForgotPassword_CtaUrl_IsUrlEncoded` — same for `SendForgotPasswordEmailHandler`.
   - Verify: `dotnet test tests/wa.workers.unit`.
3. **Route matcher (D-3)** — MODIFY `src/wa.web/src/features/auth/authRoute.ts`: add `MAGIC_LINK_PATH = '/auth'` and
   ```ts
   export function matchMagicLinkRedeem(pathname: string, search: string): string | null {
     if (pathname !== MAGIC_LINK_PATH) return null
     const raw = new URLSearchParams(search).get('ml')
     if (!raw) return null
     return raw.replace(/ /g, '+') // legacy unencoded links: '+' arrived as a space; base64 has no spaces
   }
   ```
   MODIFY `authRoute.test.ts`: `('/auth', '?ml=ab%2Bcd%2Fef%3D%3D')` → `'ab+cd/ef=='`; legacy `('/auth', '?ml=ab+cd/ef==')` → `'ab+cd/ef=='`; `('/auth', '')` → null; `('/auth', '?ml=')` → null; `('/authx', '?ml=x')` → null; `('/account', '?ml=x')` → null.
4. **Redeem page (D-5..D-7)** — CREATE `src/wa.web/src/features/auth/MagicLinkRedeemPage.tsx`, props `{ token: string; onSignedIn: (p: StoredProfile) => void }`. Module-level `type RedeemOutcome = { ok: true; profile: StoredProfile } | { ok: false; message: string }`, `const inflight = new Map<string, Promise<RedeemOutcome>>()`, `function redeemOnce(token)`: POST `${API_BASE}/auth/magic-link/redeem`, headers `{ 'Content-Type': 'application/json', traceparent: getTraceParent() }`, `credentials: 'include'`, body `JSON.stringify({ token })`; 2xx → `{ ok: true, profile: await res.json() as StoredProfile }`; non-2xx → `details ?? title ?? i18n.t('auth.error.generic')` (try/catch json, as `AuthPage.tsx:111-118`); network throw → auth.error.generic. Component: `const [error, setError] = useState<string | null>(null)`; effect on `[token, onSignedIn]`:
   ```ts
   useEffect(() => {
     let active = true
     void redeemOnce(token).then((r) => {
       if (!active) return
       if (r.ok) { writeSession(r.profile); onSignedIn(r.profile) } else setError(r.message)
     })
     return () => { active = false }
   }, [token, onSignedIn])
   ```
   Render: `error === null` → `<section className="au" aria-label={i18n.t('auth.page.ariaLabel')} aria-busy="true"><div className="au-card" /></section>`; else `<section className="au" aria-label=...><div className="au-card"><p className="au-error" role="alert">{error}</p><button type="button" className="au-ghost-btn" onClick={goToAccount}>{i18n.t('auth.ghost.magicLink')}</button></div></section>` where goToAccount = `window.history.pushState('', '', '/account'); window.dispatchEvent(new PopStateEvent('popstate'))` (App's goHome idiom, `App.tsx:91-94`). Default + named export like AuthPage.
5. **Tests** — CREATE `src/wa.web/src/features/auth/MagicLinkRedeemPage.test.tsx` (`// @vitest-environment jsdom`; createRoot + act harness and `stubFetch` from `AuthPage.test.tsx:1-33`; in-memory localStorage shim from `ProfilePage.test.tsx:21-33`; `clearSession()` in beforeEach; `vi.unstubAllGlobals()` afterEach). **Use a distinct token per test** (module cache, D-7). Facts:
   - success: fetch called once, URL ends `/auth/magic-link/redeem`, method POST, body `{"token":"<t>"}`, `credentials: 'include'` (capture `init` in the stub); after flush `readSession()` equals the DTO and `onSignedIn` called once with it.
   - 410: body `{ code: 'LINK_EXPIRED', status: 410, title: 'This link has expired', details: 'This link has expired' }` → `role=alert` text "This link has expired"; `readSession()` null; `onSignedIn` not called; button "Email me a magic link" click → `window.location.pathname === '/account'`.
   - 400: VALIDATION with details "The provided magic link token is malformed." → that text in the alert.
   - network failure (fetch rejects) → auth.error.generic text.
   - StrictMode: render `createElement(StrictMode, null, createElement(MagicLinkRedeemPage, …))` with a success stub → fetch call count 1, `onSignedIn` call count 1.
   - Verify: `npx vitest run src/features/auth`.
6. **Wire App (D-4)** — MODIFY `src/wa.web/src/App.tsx`: import `MagicLinkRedeemPage` and `matchMagicLinkRedeem`; add `const [search, setSearch] = useState(() => (typeof window === 'undefined' ? '' : window.location.search))`; `onPop` sets both `setPathname(window.location.pathname)` and `setSearch(window.location.search)`; `const magicLinkToken = matchMagicLinkRedeem(pathname, search)`; insert after the unsubscribe branch:
   ```tsx
   ) : magicLinkToken !== null ? (
     // CHORE-magic-link-redeem (FR-008-1, EC-008-3): emailed /auth?ml= link — redeems regardless of current session.
     <MagicLinkRedeemPage token={magicLinkToken} onSignedIn={handleSignedIn} />
   ```
   and hoist the AuthPage inline callback into `const handleSignedIn = useCallback((p: StoredProfile) => { setSession(p); goHome() }, [])` used by BOTH AuthPage and the redeem page (one sign-in callback; stable identity so the redeem effect does not re-run on App re-renders). `goHome` only touches window + popstate, so an empty dep list is correct; if eslint react-hooks flags `goHome`, move `goHome` above as a plain module-level function (it uses no state). Add `useCallback` to the react import.
7. **Gate:** dotnet format src/wa.slnx (verify-no-changes mode); `dotnet test tests/wa.domain.unit`; `dotnet test tests/wa.application.unit`; `dotnet test tests/wa.workers.unit`; in src/wa.web: `npm run lint && npm run test:run && npm run build`; `node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest`. No wa.api file changes → api.integration not run (compile only via the solution build). One commit: `feat: CHORE-magic-link-redeem — web /auth?ml= redeem page + URL-encoded magic-link token`.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs` | MODIFY | `:94-96` CTA → `Uri.EscapeDataString(rawToken)` + comment |
| `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs` | MODIFY | `:97-99` same |
| `tests/wa.workers.unit/Email/AuthEmailCtaUrlTests.cs` | CREATE | two facts: encoded `%2B %2F %3D` in Html + Text bodies of both handlers |
| `src/wa.web/src/features/auth/authRoute.ts` | MODIFY | add `matchMagicLinkRedeem(pathname, search)` |
| `src/wa.web/src/features/auth/authRoute.test.ts` | MODIFY | matcher cases (encoded, legacy `+`, missing/empty ml, wrong path) |
| `src/wa.web/src/features/auth/MagicLinkRedeemPage.tsx` | CREATE | redeem-once page (module promise cache), writeSession + onSignedIn, error card |
| `src/wa.web/src/features/auth/MagicLinkRedeemPage.test.tsx` | CREATE | success, 410, 400, network, StrictMode single POST |
| `src/wa.web/src/App.tsx` | MODIFY | `search` state + popstate, `handleSignedIn` useCallback, redeem branch after unsubscribe |

**Existing code to change**

`src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs:94-96`
```csharp
        // 2. Build CTA URL: {publicOrigin}/auth?ml={rawToken}
        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/auth?ml={rawToken}";
```
`src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs:97-99`
```csharp
        // 2. Build CTA URL: {publicOrigin}/auth?ml={rawToken} — same as magic-link (redeem endpoint 10 is purpose-agnostic).
        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/auth?ml={rawToken}";
```
Both handlers: ctor `(IEmailSender, IEventPublisher, IConfiguration, WaTelemetryReporter, ILogger<THandler>, TimeProvider)` (`SendMagicLinkEmailHandler.cs:37-43`); `HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)` (`:65`); payload keys `email`, `token`, `expiresAtUtc` (`:72-91`). Ctor throws if templates are missing — fine in wa.workers.unit (templates copied, `EmailAudienceClassificationTests.cs:106-112`). `IEventPublisher.PublishAsync` returns `Task<DomainEventRecord>` (`src/wa.application/Ports/IEventPublisher.cs:16`).

`src/wa.web/src/features/auth/authRoute.ts:7-11`
```ts
export const ACCOUNT_PATH_RE = /^\/account(\/.*)?$/

export function matchAccountPath(pathname: string): boolean {
  return ACCOUNT_PATH_RE.test(pathname)
}
```

`src/wa.web/src/App.tsx` (relevant lines)
```tsx
1   import { useEffect, useState } from 'react'
18  import { matchAccountPath } from './features/auth/authRoute'
44    const [pathname, setPathname] = useState(() => (typeof window === 'undefined' ? '/' : window.location.pathname))
55    useEffect(() => {
56      const onPop = () => setPathname(window.location.pathname)
57      window.addEventListener('popstate', onPop)
58      return () => window.removeEventListener('popstate', onPop)
59    }, [])
70    const unsubscribeToken = matchUnsubscribePath(pathname)
91    const goHome = () => {
92      window.history.pushState('', '', '/')
93      window.dispatchEvent(new PopStateEvent('popstate')) // existing popstate listener updates pathname.
94    }
137           {unsubscribeToken !== null ? (
138             <UnsubscribePage token={unsubscribeToken} />
139           ) : session && matchProfilePath(pathname) ? (
151           ) : matchAccountPath(pathname) ? (
154             <AuthPage onSignedIn={(p) => { setSession(p); goHome() }} />
```
`StoredProfile` is already imported as a type at `App.tsx:5`. The SSR test (`App.test.tsx`, renderToStaticMarkup without window) relies on the typeof-window guards — keep the new `search` initialiser guarded the same way.

Reused (no change): `writeSession` `src/wa.web/src/core/auth/session.ts:43`; API_BASE in `src/wa.web/src/core/config/apiBase.ts:22`; `getTraceParent` `src/wa.web/src/core/traceparent.ts`; i18n keys auth.error.generic (`en.json:112`), auth.ghost.magicLink (`en.json:115`), auth.page.ariaLabel (`en.json:202`) — present in all 8 locale files.

## 5. Open Questions / Risks

- **R-1 Copy is English-only for errors** — the 410/400 text comes from the server Problem+JSON `details`, exactly like every AuthPage error today (`AuthPage.tsx:110-119`). Localizing LINK_EXPIRED would need new i18n keys in all 8 locales — out of scope; follow-up if wanted.
- **R-2 "Send a new link" wording** — EC-008-3 names a "Send a new link" button; we reuse "Email me a magic link" (auth.ghost.magicLink) to avoid new keys. Behavior matches (leads to the request form); exact label differs.
- **R-3 Pre-existing, not changed:** `writeSession(dto)` persists the whole response DTO, including `sessionToken`, in localStorage (AuthPage does the same at `AuthPage.tsx:104-105`). The redeem page mirrors it by D-5. Stripping `sessionToken` client-side is a security-shaped change to both flows → separate chore if wanted (not escalated here: no behavior change introduced).
- **R-4 Secure cookie on local http** — the redeem endpoint sets `wa.session` with `Secure = true` (`AuthEndpoints.cs:96`), same as login; browsers treat `localhost` as a secure context so this works locally, as login already does.
- **R-5 `/auth` without `ml`** stays a 404 (D-3). If product wants `/auth` to alias `/account`, that is a one-line follow-up.
