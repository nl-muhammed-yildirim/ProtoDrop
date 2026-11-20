# REPO-FACTS — living snapshot for agents (updated in every code commit's docs step)

**Purpose:** Architect reads this instead of exploring from scratch. Coder cites it. Reviewer checks against it.
**Rule:** any API shape below that changes MUST change here in the same commit; `verify_plan.py` fails on drift between plans and these facts.
**Last updated:** 2026-11-19 (F-PRF-001 branding close-out; 2026-11-17: Phase 0 gap sweep: endpoint table, telemetry count, token location; T-014-05 accountLocale key)

## 1. Closed sets (counts are load-bearing — cite them, don't re-enumerate in plans)

### ProblemCodes (`src/wa.domain/Errors/ProblemCodes.cs`) — **26 members**
`VALIDATION`, `UNAUTHENTICATED`, `FORBIDDEN`, `NOT_FOUND`, `TRANSFER_NOT_FINALIZED`,
`TRANSFER_SIZE_EXCEEDED`, `STORAGE_QUOTA_EXCEEDED`, `MAX_DOWNLOADS_REACHED`, `WRONG_PASSWORD`,
`TRANSFER_EXPIRED`, `TRANSFER_DELETED`, `FILES_GONE`, `IDEMPOTENCY_CONFLICT`, `RATE_LIMITED`,
`STRIPE_WEBHOOK_MISMATCH`, `EMAIL_UNSUBSCRIBED`, `LINK_EXPIRED`, `BILLING_UNAVAILABLE`,
`ACTIVE_TRANSFERS_EXCEEDED`, `FLAG_INVALID`, `FLAG_UNKNOWN`, `FEATURE_DISABLED`, `EXPORT_IN_FLIGHT`,
`LOGO_TOO_LARGE`, `LOGO_TYPE_INVALID`, `INTERNAL`.

### Telemetry (`src/wa.domain/Telemetry/TelemetryEvents.cs`) — **26 events + 7 metrics = 33 names** (TA-10.2, "names are code")
Events: `upload_started`, `upload_completed`, `upload_failed`, `transfer_page_viewed`,
`transfer_not_found`, `password_correct`, `password_wrong`, `download_started`,
`download_completed`, `zip_generated`, `zip_failed`, `email_sent`, `email_failed`,
`user_created`, `login_success`, `login_failed`, `account_deleted`, `plan_changed`,
`translation_missing`, `admin_action`, `dlq_count`, `email.template_fallback`, `template_render_error`,
`branding_logo_missing`, `ad_impression`, `ad_error` (client-only, F-PRF-004).
Metrics: `upload.success_rate`, `link_to_download_seconds`, `expiry_job_lag_seconds`,
`email_failures_1h`, `active_transfers`, `storage_bytes_active`, `api.requests{status}`.

### CSS custom properties — **22 tokens** (14 color + 8 typography/shape)
**Colors live in `src/wa.web/src/styles/tokens.css`** (T-015-01): all 14 defined in each of `:root` (light default), `html[data-theme='dark']` and `html[data-theme='light']`; gated by `scripts/check-theme-tokens.mjs` + `check-contrast.mjs`. Typography/shape stay in `src/wa.web/src/index.css` `:root`. Light values:
Colors: `--bg #FFFFFF`, `--bg-subtle #F5F6F8`, `--bg-elevated #FFFFFF`, `--fg #101418`,
`--fg-muted #5F6B7A`, `--fg-faint #93A0AF`, `--accent #4185F4`, `--accent-hover #2F6FD8`,
`--accent-soft #EAF1FD`, `--border #DDE2E8`, `--danger #D6453D`, `--danger-soft #FBEBEA`,
`--success #2E9E5B`, `--warning #C77C11`.
Typography/shape: `--fs-display 40px`, `--fs-h1 28px`, `--fs-body 16px`, `--fs-small 14px`,
`--fs-tiny 12px`, `--radius-sm 6px`, `--radius-md 10px`, `--radius-lg 16px`.

## 2. API surface (`src/wa.api/Program.cs`) — **43 route+method mappings** (end of Phase 0 = 36; +3 F-BIL-002: checkout, portal, Stripe webhook; +1 F-BIL-003 US-020-03: `GET /billing/plan`; 39 always-on + 1 Development-only; +3 F-PRF-001 US-021-04: `GET/PUT/DELETE /auth/me/logo` = 42 always-on + 1 Development-only)

| Area | Route | Method |
|---|---|---|
| Infra | `/` · `/health` | GET · GET |
| Infra (Development only) | `/api/v1/dev/chaos?kind=internal\|rate_limited` | GET |
| Upload/send | `/api/v1/transfers/draft` | POST |
| | `/api/v1/transfers/draft/{draftId}/finalize` | POST |
| | `/api/v1/transfers/{id}/send` | POST |
| | `/api/v1/transfers/{id}/resend` | POST |
| My Files (owner) | `/api/v1/transfers` | GET |
| | `/api/v1/transfers/{id}` | GET / DELETE |
| Public (recipient) | `/api/v1/public/transfers/{linkId}` | GET |
| | `/api/v1/public/transfers/{linkId}/unlock` | POST |
| | `/api/v1/public/transfers/{linkId}/files/{fileId}/download-url` | GET |
| | `/api/v1/public/transfers/{linkId}/download-all-url` | GET |
| Email | `/unsubscribe/{token}` | GET |
| Auth | `/api/v1/auth/signup` · `/magic-link` · `/magic-link/redeem` · `/forgot-password` · `/login` · `/logout` | POST (each) |
| | `/api/v1/auth/me` | GET / PATCH / DELETE |
| | `/api/v1/auth/me/logo` | GET / PUT / DELETE |
| Admin | `/api/v1/admin/overview` · `/audit-recent` · `/suppressions` · `/transfers` · `/users` · `/flags` | GET (each) |
| | `/api/v1/admin/suppressions/{id:guid}` · `/transfers/{id:guid}` · `/users/{id:guid}` | DELETE (each) |
| | `/api/v1/admin/users/{id:guid}` | PATCH |
| | `/api/v1/admin/flags/{key}` | PUT |

## 3. Auth & session wiring facts

- **Session cookie:** name `wa.session`, HttpOnly, set by endpoints (NOT middleware); JS cannot read it. Client mirrors the profile DTO in localStorage under key **`protoDrop.session`** (`src/wa.web/src/core/auth/session.ts:18`).
- **Middleware order** (`Program.cs:163–171`): Cors → Correlation → IpRateLimiting → Idempotency → Session (reads cookie, sets `HttpContext.Items['wa.session.userId']`, refreshes < 15 d remaining) → GlobalError (business codes keep status; others 500 INTERNAL) → SerilogRequestLogging.
- **CORS** (`src/wa.api/Middleware/CorsAllowListMiddleware.cs`): allowlist from config key `Wa:Cors:AllowedOrigins` (comma-separated); echoes exact origin + `Vary: Origin`, never `*`; preflight answered by middleware with 1 h cache, allowed headers `Authorization, Content-Type, Idempotency-Key`.
- **Stores:** `IAppUserStore` (`src/wa.application/UseCases/Auth/IAppUserStore.cs`) — `FindByEmailAsync`, `CreateAsync`, `GetByIdAsync` (null when unknown OR soft-deleted), `UpdateProfileAsync` (null arg = leave column alone; empty displayName → email local part), `SoftDeleteWithReHomeAsync` (one transaction, returns false on unknown id).
  `IAuthTokenStore` — `FindByHashAsync(sha256Hex)`, `CreateAsync(AuthToken)`, `MarkRedeemedAsync` (first redeem wins).
- **Magic-link token:** stored as SHA-256 hex (64 chars), single-use via `RedeemedAtUtc`.

## 4. Storage keys & client constants

| Key | Value | Location |
|---|---|---|
| Session mirror | `protoDrop.session` | web localStorage |
| CSRF/idempotency header | `Idempotency-Key` | CORS-allowed header |
| Account language (AC-014-3) | `protoDrop.accountLocale` — one of the 8 locales; absent = browser → en; survives sign-out; only `core/i18n/init.ts` (`readAccountLocale`/`setAccountLocale`) touches it | web localStorage |

## 5. Maintenance rules (for the Coder docs step)

1. Added/removed ProblemCode, telemetry name, CSS token, or endpoint → update §1–§2 **in the same commit**.
2. Counts in headers are load-bearing; if a plan cites an old count, `verify_plan.py` flags it.
3. Keep this file under ~150 lines — it is read into every architect context.
