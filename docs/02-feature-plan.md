# Feature Plan — ProtoDrop File Transfer Platform

**Version 0.6** | **Last updated:** 2026-08-30
**Stack:** .NET 10 (Minimal API) · Clean (onion) architecture · CQRS via MediatR 12.5 · Azure · React 18 + TypeScript (Vite) · Visual Studio 2026
**Companion docs:** `01-product-analysis.md` (product intent), `03-technical-architecture.md` (TA-* sections), `features/` (per-feature spec + user story files)

---

## 0. How to Use This Document with AI

### 0.1 Reference convention

- Every feature has a stable ID: `F-<AREA>-###` (e.g., `F-TRF-002`).
- Inside a feature, requirements are `FR-<id>-n`, acceptance criteria `AC-<id>-n`, edge cases `EC-<id>-n`.
- When prompting an AI, always cite IDs: *"Implement F-TRF-002, FR-002-1 through FR-002-5. Do not implement beyond this feature."*
- **Detailed spec:** every feature now has a dedicated file in `features/` (feature description, FR/AC/EC, UI notes, technical notes, test plan) and **one file per user story**. `features/README.md` is the index.

### 0.2 Recommended prompt template

```text
You are implementing feature {FEATURE_ID} from our feature plan.

STACK: .NET 10 (Minimal API), clean/onion architecture, CQRS via MediatR 12.5,
EF Core 10 + Azure SQL, Azure Blob Storage + SAS, Azure Service Bus,
Azure Functions, React 18 + TypeScript frontend,
Azure Communication Hub for email, Application Insights for telemetry.
IDE: Visual Studio 2026.

SCOPE: Implement ONLY {FEATURE_ID} per its spec file in features/ (see features/README.md index for the exact path).
Reference the global Limits table (Appendix A) and the shared Entities list
(Appendix B). Do not invent features from other IDs.

REQUIREMENTS: Implement FR-{id}-1..n.
ACCEPTANCE: You must satisfy AC-{id}-1..m (Gherkin). Add unit + integration
tests that encode each AC.
EDGE CASES: Handle EC-{id}-1..n explicitly (each must have a test).
USER STORIES: See the feature's folder in features/ — each story file there must
be traceable to the delivered behavior.

DEFINITION OF DONE: code + tests + migration (if schema changes) +
telemetry events named per Appendix C.
```

### 0.3 Definition of Done (applies to every feature)

1. Code compiles; all `AC-` scenarios have passing automated tests.
2. EF Core migration committed for any schema change.
3. Telemetry events emitted exactly as named in Appendix C.
4. No new external dependencies without noting them in the PR description.
5. i18n: all new user-facing strings added to translation files (not hardcoded).

### 0.4 Status of limits

Values marked **TBD-LAUNCH** are ProtoDrop-typical defaults and must be confirmed against your pricing decision before launch, but build them as **constants in code** (never hardcode in multiple places).

### 0.5 Change log

- **v0.6 (2026-08-30):** `features/` reorganized into **Phase > Feature** folders: `features/<Phase <n>-<name>>/<feature-id>/<feature spec + user story files>.md` (e.g. `features/Phase 0-MVP/F-TRF-001/F-TRF-001-upload-surface.md` + its `US-001-*.md` files). The flat `user-stories/` folder is gone — each story now sits next to its feature. All index/README references updated; relative story→spec links simplified (same folder).
- **v0.5 (2026-08-28):** Phase X (cross-cutting) fully specced in `features/`: F-XCT-001…005 — FR/AC/EC + UI/technical notes + test plans, and one user-story file per story (US-044…048, 11 stories). ID scheme: F-XCT-001 → 044 … F-XCT-005 → 048. New flag keys seeded: `feature.search`, `feature.dataExport`, `feature.emailSettings` (F-XCT-001 FR-044-1); new Problem+JSON codes `FEATURE_DISABLED`, `FLAG_UNKNOWN`, `FLAG_INVALID`, `Q_TOO_LONG`, `EXPORT_IN_FLIGHT`. New tasks T-065…T-069 in `Milestone-Backlog.md` (M7). Open decision D-21 (search backend confirmation).
- **v0.4 (2026-08-28):** Phase 3 fully specced in `features/`: F-ENT-001…006 — FR/AC/EC + UI/technical notes + test plans, and one user-story file per story (US-038…043, 27 stories). ID scheme: F-ENT-001 → 038 … F-ENT-006 → 043. Org layer DDL (Organization/OrgMember/OrgAuditEntry/IdpConfig/ScimGroup/StorageAccount/ResidencyAnomaly/OrgDomain/Workspace) defined in the feature files. New Phase 3 tasks T-056…064 in `Milestone-Backlog.md` (M6).
- **v0.3 (2026-08-28):** Phase 1 fully specced in `features/`: F-BIL-001…003 + F-PRF-001…004 — FR/AC/EC + UI/technical notes + test plans, and one user-story file per story (US-018…024, 31 stories). ID scheme: Phase 1 continues Phase 0 numbering per feature (F-BIL-001 → 018 … F-PRF-004 → 024). New Phase 1 tasks T-031…040 in `Milestone-Backlog.md`.
- **v0.2 (2026-08-23):** Stack updated to .NET 10 / clean (onion) / MediatR 12.5 / VS 2026. Added `features/` directory: one spec file per feature + one file per user story.
- **v0.1:** initial full spec.

---

## 1. Phase Map

| Phase | Product | Feature IDs | Detail level |
|---|---|---|---|
| 0 — MVP | Core transfer, free tier | F-TRF-001 … F-TRF-017 | **Full spec (implement these)** — files in `features/` |
| 1 — Pro | Billing, branding, scheduling, analytics | F-BIL-001…, F-PRF-001… | Full spec |
| 2a — Collect | Many→one file collection | F-COL-001… | **Full spec** — files in `features/Phase 2-Collect-Sign-Albums/` (M5) |
| 2b — Sign | E-sign documents | F-SGN-001… | **Full spec** — files in `features/Phase 2-Collect-Sign-Albums/` (M5) |
| 2c — Albums | Shared galleries | F-ALB-001… | **Full spec** — files in `features/Phase 2-Collect-Sign-Albums/` (M5) |
| 3 — Enterprise | SSO, SCIM, residency, subdomains, orgs, workspaces | F-ENT-001… | **Full spec (implement these)** — files in `features/` |
| X — Cross-cutting | Flags, search, email settings, GDPR export/erasure, PII policy | F-XCT-001…005 | **Full spec** — files in `features/` (US-044…048); i18n/a11y/dark-mode/mobile live in F-TRF-014…017 |

---

## 2. Phase 0 — Core Transfer (MVP)

**Phase goal:** a guest with a 4 GB file can drag it in, get a link, and a recipient with zero context can download it from a phone — with everything expiring cleanly.

**Phase non-goals (explicitly out of scope):** desktop/mobile native apps, virus scanning, file previews/thumbnails (P1), ads (P1), Collect/Sign/Albums, team accounts, resume upload across browser sessions, file compression on upload.

> **Per-feature detail lives in `features/Phase 0-MVP/F-TRF-00X*/` (+ user story files).**
> The entries below are the canonical FR/AC/EC sets; the feature files restate and extend them with UI notes, technical notes, and test plans.

### F-TRF-001 — Upload Surface (chunked upload) — **P0**

*User story:* As a guest, I want to drop files on the page and see progress, so that I never guess whether my transfer works.

**Functional requirements**

- FR-001-1: Landing page is a single full-viewport drag-and-drop surface. No hero, no feature grid. Drop zone accepts via drag, click-to-browse, and paste (image).
- FR-001-2: Multi-file selection. Files are added to a staging list; user removes individual files before final "Send."
- FR-001-3: Total selected size is validated against the plan limit (Limits: `MAX_TRANSFER_SIZE`) **before upload starts**; per-file size limit also enforced (`MAX_SINGLE_FILE`).
- FR-001-4: Upload is **chunked** (block-blob / multipart via `@azure/storage-blob`), block size ~8 MB, parallelism 4–8.
- FR-001-5: Progress UI shows per-file % and overall % (based on bytes uploaded, not blocks).
- FR-001-6: Upload failure on a block → retry that block up to 5 times with backoff; then mark file failed with a per-file "Retry" button; successful blocks are kept (block-level resume within session).
- FR-001-7: Uploads above a configurable threshold (`CHUNK_THRESHOLD`, default 10 MB) are never a single PUT.

**Acceptance criteria**

```gherkin
AC-001-1: Guest drops 3 files totaling 2 GB
  When upload completes
  Then all files show 100% and "Send" becomes enabled

AC-001-2: User selects files exceeding MAX_TRANSFER_SIZE
  When total exceeds the limit before Send
  Then a message names the limit and upload has not started
  And no bytes were written to storage

AC-001-3: Network drops mid-upload
  When a block fails after 5 retries
  Then that file is marked failed, other files unaffected, and a Retry button appears

AC-001-4: User drops an empty folder
  Then it is silently skipped with a toast "Some files were skipped"
```

**Edge cases**

- EC-001-1: File renamed on disk after selection (browser hands a `File` object — upload continues, note this in UI copy: "we copy the file now").
- EC-001-2: Duplicate file names in one transfer → keep both, names preserved (collision handling is the *recipient's* problem on download — see F-TRF-004).
- EC-001-3: 0-byte file: accept but flag in file list.
- EC-001-4: `file://` / HEIC files: accept any MIME; do not sniff.

**Technical notes**

- Flow: `POST /transfers/draft` → returns `TransferDraft` + a **write-scoped SAS** (container-scoped, `PUT,READ`, 2 h TTL) scoped to `uploads/{transferId}/`.
- Browser uploads blocks **directly to Blob** — the API never proxies file bytes.
- `POST /transfers/draft/{id}/finalize` → API commits: moves/links blobs to `transfers/{id}/files/{fileId}`, sets `Transfer.Status = Completed`, emits `transfer.created` (Appendix C).
- Staging blobs older than 24 h without finalize → lifecycle deletion.
- Telemetry: `upload_started`, `upload_completed` (with bytes, duration, retry_count), `upload_failed`.

**API**

- `POST /api/v1/transfers/draft` → `{ draftId, sas, uploadUrlPrefix }`
- `POST /api/v1/transfers/draft/{draftId}/finalize` → `TransferDto`

---

### F-TRF-002 — Transfer Creation & Link — **P0**

*User story:* As a sender, I want a link I can paste anywhere, without a URL bar full of tracking noise.

**FR**

- FR-002-1: On finalize, the transfer gets a **link ID**: 8 characters, Crockford base32, no ambiguous chars (`0/O/1/I`), globally unique.
- FR-002-2: Link format: `{origin}/t/{linkId}` (e.g., `wetransfer.com/t/3f9k2a7x`).
- FR-002-3: The "link screen" shows: copy button (with success state), recipient email field(s), optional password field, "Send transfer" button.
- FR-002-4: Transfer metadata: sender name (required text, defaults to "Anonymous" if guest), sender email (if account user, else optional field), optional note message.
- FR-002-5: Recipient emails are validated per-address (comma/space separated accepted, auto-normalized).
- FR-002-6: "Send transfer" with zero emails is allowed — link-only transfer.
- FR-002-7: After send, sender sees a confirmation screen with the link and "Copy" plus "Send again" (creates a *new* transfer draft with same files — files are copied, not aliased).

**AC**

```gherkin
AC-002-1: Finalize a transfer
  Then it receives an 8-char Crockford linkId
  And GET /t/{linkId} returns the recipient page with HTTP 200

AC-002-2: Send with emails "a@x.com, b@y.com "
  Then 2 valid addresses are stored (trailing space ignored)
  And one email is sent per address (see F-TRF-006)

AC-002-3: Send with 0 emails
  Then transfer is active and link-only

AC-002-4: Collision on linkId (astronomically unlikely)
  Then server regenerates once; still collision → 500 + telemetry
```

**Edge cases**

- EC-002-1: Same user finalizes twice (double-click) → `Idempotency-Key` header on finalize; duplicate returns the original transfer.
- EC-002-2: Guest sender email invalid at send → address rejected inline, transfer not blocked.

**Technical notes**

- `Transfer` entity: `Id (uuid)`, `LinkId (unique, indexed)`, `Status (Draft|Active|Expired|DownloadedLimit|Deleted)`, `ExpiresAt`, `MaxDownloads`, `DownloadsCount`, `PasswordHash?`, `SenderName`, `SenderEmail?`, `Note?`, `TotalBytes`, `CreatedAt`.
- `FileItem`: `Id`, `TransferId`, `BlobName`, `OriginalName`, `SizeBytes`, `ContentType`, `SortOrder`.
- `EmailRecipient`: `Id`, `TransferId`, `Address (unique per transfer)`, `NotifiedAt`.
- Blob layout: `transfers/{transferId}/files/{fileId}` (no folder nesting; the folder ID is the security boundary, not a name).
- Emits `transfer.created` → email worker + metrics.

**API**

- `POST /api/v1/transfers/draft/{draftId}/finalize` (carries `Idempotency-Key`)
- `POST /api/v1/transfers/{id}/send` — `{ emails[], password?, note?, senderName? }`
- `GET /api/v1/transfers/{id}`

---

### F-TRF-003 — Recipient Download Page — **P0**

*User story:* As a recipient with a link, I want to download files in one tap, no account, even on a phone.

**FR**

- FR-003-1: `GET /t/{linkId}` renders the recipient page: "From: {senderName}", optional note, file list (name + human size + type icon), and a **Download** button per file plus **Download all** when >1 file.
- FR-003-2: **No account required.** No "Sign up to view" walls.
- FR-003-3: Download uses a **signed read URL** (time-boxed SAS, default 30 min) so the direct link is hotlink-able by the browser for the whole session but not permanently.
- FR-003-4: Download-all produces a **zip** generated server-side (see F-TRF-004); single file streams the file directly with original filename.
- FR-003-5: Page shows remaining downloads (`Downloads left: 87`) once the transfer is past its first download and `MaxDownloads < ∞`.
- FR-003-6: Password gate (if set): page is a single password field; wrong password → shake + "Wrong password", no rate-limit lockout in MVP (but log).
- FR-003-7: After a successful full download, a **growth-loop panel** appears: "Send something" → drops to upload surface (F-TRF-001) pre-filled with nothing.
- FR-003-8: Expired transfer → dedicated screen: "This transfer has expired" + sender's email (if provided) + "Send something" button. No file list, no byte leak.
- FR-003-9: Unknown linkId → same as expired (deliberately identical to avoid ID enumeration; AC-003-9 enforces byte-identical response except status 404 vs 410).
- FR-003-10: Mobile: single column, sticky download button, works in mobile Safari (watch out: iOS downloads need `Content-Disposition` fallbacks).

**AC**

```gherkin
AC-003-1: Recipient opens an active link
  Then file list renders with human-readable sizes (< 1000 ms TTI on 4G)
  And clicking "Download all" downloads a zip containing every file with original names

AC-003-2: Password-protected link
  When the page loads
  Then only the password field is visible
  And a correct password unlocks the list and sets a sessionStorage token
  So a browser refresh does not re-prompt

AC-003-3: Open an expired link
  Then the expired screen shows, files are not listed, and no SAS is minted

AC-003-4: Open a never-existed link
  Then the response is indistinguishable from expired except HTTP status (404 vs 410)
  And Application Insights captures a "transfer_not_found" event

AC-003-5: Download limit reached
  Then the page shows "All downloads have been used" with sender email
```

**Edge cases**

- EC-003-1: Recipient with 500 MB file on flaky 3G: browser-native resume via Range requests against the SAS URL (Blob SAS supports Range).
- EC-003-2: Password session token must not survive `sessionStorage` clear → re-prompt; that's fine.
- EC-003-3: File names with Unicode / spaces / quotes → original bytes preserved in `Content-Disposition` filename\* (RFC 5987).

**Technical notes**

- Password verify: PBKDF2 (or ASP.NET Identity's `PasswordHasher`) with random salt per transfer; constant-time compare.
- Password unlock: `POST /t/{linkId}/unlock` → `{ token }` JWT (HS256, 7-day exp, scoped to transferId); subsequent file requests accept `?t={token}` or header.
- Download counting: **only** successful (206/200 completed) downloads count, not started downloads → use a post-completion heuristic: count when the SAS is minted AND a `download_started`+`download_completed` telemetry pair closes within 15 min; in MVP, simplify: count at SAS mint, accept over-count, document as known imprecision (refine in P1).
- Telemetry: `transfer_page_viewed`, `download_started`, `download_completed`, `password_correct`, `password_wrong`.

**API**

- `GET /t/{linkId}` (public HTML/SPA route; data via `GET /api/v1/public/transfers/{linkId}`)
- `POST /api/v1/public/transfers/{linkId}/unlock` → `{ token }`
- `GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url` → `{ url, expiresAt }`
- `GET /api/v1/public/transfers/{linkId}/download-all-url` → `{ url }` (zip)

---

### F-TRF-004 — Download All (ZIP) — **P0**

*User story:* As a recipient, one click gives me the whole set.

**FR**

- FR-004-1: "Download all" available only when transfer has ≥2 files.
- FR-004-2: ZIP is generated **server-side on demand** by an Azure Function: reads files, writes `transfers/{id}/all.zip`, returns its download URL.
- FR-004-3: Generated ZIP is **cached for the life of the transfer** (same path; regenerate only if any file changes — in MVP transfers are immutable, so cache is forever-per-transfer).
- FR-004-4: Total zip size cap: `MAX_ZIP_SIZE` (default 4 GB). If a transfer exceeds it, "Download all" is hidden; per-file only, with an inline note.
- FR-004-5: ZIP contains entries with the original file names (top-level, no folder). Name collisions inside the zip: append `_1`, `_2`.
- FR-004-6: Generation is async: first click returns 202 with `?wait=true` polling every 2 s; UI shows "Preparing your download…".

**AC**

```gherkin
AC-004-1: 3-file transfer, click Download all
  Then a zip is generated once
  And a second recipient (or same user, second click) gets the cached zip instantly
  And exactly one zip blob exists for the transfer

AC-004-2: Transfer totals 6 GB
  Then "Download all" is hidden with note "Files are large — download individually"
```

**Edge cases**

- EC-004-1: Zip generation OOM on very large totals → generate in a streaming `Stream` copy, 4 MB buffer, never buffer whole zip.
- EC-004-2: Zip of a transfer that expires while generating → delete partial zip in a `finally`.

**Technical notes**

- Function: `f-zip` (plain, not Durable), 10-min timeout, 4 GB streaming cap. Trigger: HTTP. Idempotent by path.
- Emits `zip.generated` (duration, size).

---

### F-TRF-005 — Expiry & Auto-Deletion — **P0**

*User story:* As the operator, I pay storage only for transfers people actually use.

**FR**

- FR-005-1: Transfers expire at `CreatedAt + RETENTION_DAYS` (default 7).
- FR-005-2: Expiration sets `Status = Expired`; page behavior unchanged (F-TRF-003-8).
- FR-005-3: **Deletion** (blobs + rows) happens at `Expired + GRACE_DAYS` (default 3) so a just-expired link can be revived by re-send (F-TRF-010).
- FR-005-4: Also expire early if `DownloadsCount >= MaxDownloads` → `Status = DownloadLimit`.
- FR-005-5: Expiry job: timer function, runs every 15 min, scans `expires_at < now()` index, batches 500, updates + emits `transfer.expired`.
- FR-005-6: Deletion job: scans `Status=Expired AND expired_at + grace < now()`, deletes blob container files, then row. Row deletion emits `transfer.deleted`.
- FR-005-7: Both jobs must be **idempotent** (safe to re-run; use `processed_at` sentinel / `JobRun` claim).
- FR-005-8: Blob Lifecycle Management rules are a *safety net only* (delete anything in `transfers/*` older than 30 days) — the jobs are primary.

**AC**

```gherkin
AC-005-1: Transfer created now
  Then at expiry it is marked Expired within 15 min
  And at expiry+grace its blobs are deleted

AC-005-2: Expiry job runs twice in a row
  Then no duplicate events, no double-delete, no error

AC-005-3: Transfer hits download cap before time expiry
  Then it is DownloadLimit immediately (next page view), and expiry job cleans it up on schedule
```

**Edge cases**

- EC-005-1: Clock skew between SQL and Function host → jobs use UTC only; tolerance ±2 min.
- EC-005-2: Deletion of blobs that were already deleted by lifecycle → swallow `BlobNotFound`.

---

### F-TRF-006 — Email Notifications — **P0**

*User story:* As a recipient, I get one clear email with the link.

**FR**

- FR-006-1: On `transfer.created` (when emails were provided): one email **per address** via Azure Communication Hub.
- FR-006-2: Subject: `Transfer from {senderName}`. Body (plain + inline HTML, minimal): sender name, file list (names + sizes), single CTA button "Download files", footer "This link expires in N days."
- FR-006-3: Exclude the sender's own address from notifications if provided in the recipient list.
- FR-006-4: Failed delivery → retry 3× (1 m / 10 m / 1 h) on a Service Bus dead-letter; final failure → `email_failed` telemetry + admin alert.
- FR-006-5: Unsubscribe link in footer ("You received this because someone sent you a transfer") → suppresses future emails from that sender (per-sender suppression table).
- FR-006-6: No email for link-only transfers.
- FR-006-7: All emails use the transfer sender's email as `Reply-To` if present.

**AC**

```gherkin
AC-006-1: Send transfer to 2 addresses including the sender's own
  Then exactly 1 email is sent

AC-006-2: Communication Hub returns 429
  Then the message is retried 3 times with backoff, and a 4th failure dead-letters it

AC-006-3: Recipient clicks unsubscribe
  Then subsequent transfers from the same sender email do not notify them
  And the suppression is visible in the admin dashboard
```

**Edge cases**

- EC-006-1: More than `MAX_EMAILS` (default 20) addresses → UI enforces; API also caps.
- EC-006-2: `+1` or `+2` Gmail alias addresses → treated as distinct (document).

**Technical notes**

- Worker: `f-email` (Function on `core/email` subscription). All email templates live in one place: `Emails/templates/*.html` with Stubble (Mustache) — never inline in code.
- Telemetry: `email_sent`, `email_failed`, `email_opened` (optional tracking pixel, gated by a11y/privacy — default OFF).

---

### F-TRF-007 — Free-Tier Limits — **P0**

*User story:* As the business, the free tier acquires users without eating us alive.

**FR**

- FR-007-1: All limits come from a **Limits configuration** (feature-flagged, per-plan). Defaults:

| Constant | Free default | Note |
|---|---|---|
| `MAX_TRANSFER_SIZE` | 5 GB | per transfer, total |
| `MAX_SINGLE_FILE` | 5 GB | |
| `MAX_ZIP_SIZE` | 4 GB | |
| `RETENTION_DAYS` | 7 | |
| `GRACE_DAYS` | 3 | post-expiry deletion |
| `MAX_DOWNLOADS` | 100 | per transfer |
| `MAX_EMAILS` | 20 | per transfer |
| `STORAGE_QUOTA_FREE` | 5 GB | per account (active transfers) |
| `ACTIVE_TRANSFERS_MAX_FREE` | 20 | concurrent |

- FR-007-2: Limits enforced **server-side** at finalize and at send (never trust the client), and **client-side pre-check** for UX (F-TRF-001-3).
- FR-007-3: When a limit is hit, the UI shows the exact limit and the action to upgrade (in P1; in MVP: "limit reached" screen).
- FR-007-4: Account storage quota: sum of `TotalBytes` of `Status in (Active, DownloadLimit)` ≤ `STORAGE_QUOTA_FREE`. New finalize while over quota → "Storage full" screen.
- FR-007-5: All limits editable via feature-flag service without deploy (Appendix C flag IDs).

**AC**

```gherkin
AC-007-1: Free user finalizes 5.2 GB
  Then finalize succeeds but send fails with "Transfer size 5.2 GB exceeds 5 GB limit"
  Unless plan is raised via flag in the same request (test with flag override)

AC-007-2: Account reaches 5 GB active storage
  Then the next finalize is rejected with "Storage full"
  And the admin screen shows the meter at 100%
```

**Edge cases**

- EC-007-1: Raising `MAX_TRANSFER_SIZE` flag mid-flight → only affects new transfers (documented).
- EC-007-2: Quota computed with a transactional read; concurrent sends may race by one transfer (acceptable; document).

---

### F-TRF-008 — Accounts (sign-up / sign-in) — **P0**

*User story:* As a user who sends a lot, I want history without giving ProtoDrop my life.

**FR**

- FR-008-1: Auth: **email + password** (PBKDF2 via ASP.NET Identity `PasswordHasher`) and **magic link** (email a 10-min login token). No OAuth in MVP.
- FR-008-2: Session: cookie (httpOnly, SameSite=Lax), 30-day rolling.
- FR-008-3: Sign-up = sign-in (same form; unknown email → create). Email is the identifier.
- FR-008-4: Account profile: display name (used as default "From" on all transfers), optional public display, plan (free by default).
- FR-008-5: Password rules: min 8 chars, no common-password check in MVP.
- FR-008-6: "Forgot password" → magic-link email (reuses email worker).
- FR-008-7: Transfers created while signed in are attributed to the account → appear in history (F-TRF-009).
- FR-008-8: Delete account (GDPR) → deletes user, re-homes active transfers to `SenderName`-only (anonymous), deletes profile.

**AC**

```gherkin
AC-008-1: New user enters email + password
  Then account is created, session cookie set, profile screen shows

AC-008-2: Magic link clicked twice
  Then the second click is invalid (token single-use)

AC-008-3: Signed-in user creates a transfer
  Then the transfer is listed under their "My Files"
  And "From" defaults to their display name
```

**Edge cases**

- EC-008-1: Email case-sensitivity → store lowercased; compare case-insensitive.
- EC-008-2: Account deleted while a transfer is active → transfer survives as anonymous (recipient page unaffected).

**Technical notes**

- Identity schema: custom `AppUser` + `AuthToken` tables (ADR-007: no ASP.NET Identity tables in MVP).
- Magic link: JWT in email, 10-min TTL, single-use (mark `RedeemedAtUtc`).
- Telemetry: `user_created`, `login_success`, `login_failed`, `account_deleted`.

---

### F-TRF-009 — My Files (history) — **P0**

*User story:* As a signed-in user, I can find my transfers and re-send them.

**FR**

- FR-009-1: "My Files" list: transfers the user created, most recent first; each row: file count, total size, recipient count, status, expiry countdown, download count.
- FR-009-2: Pagination: cursor-based, 25/page.
- FR-009-3: Row actions: copy link, view (recipient page), re-send (F-TRF-010), delete (with confirm; deletes blobs+row immediately, no grace).
- FR-009-4: Filter: All / Active / Expired.
- FR-009-5: Empty state with a CTA to the upload surface.

**AC**

```gherkin
AC-009-1: User with 30 transfers
  Then page 1 shows 25, ordered by CreatedAt desc

AC-009-2: Delete a transfer
  Then confirm dialog names the file count and size
  And after delete, row is gone and blobs removed

AC-009-3: Expired row
  Then countdown replaced by "Expired {date}"
```

**Edge cases**

- EC-009-1: Deleting while a download is in progress → allow; recipient's in-flight SAS still works (SAS outlives row, note this).
- EC-009-2: Two browser tabs deleting the same transfer → idempotent delete (404 on second is fine).

---

### F-TRF-010 — Re-send Transfer — **P0**

*User story:* A link expired or I got the email wrong — let me send the same files again in one click.

**FR**

- FR-010-1: "Re-send" on any non-deleted transfer: creates a **new** transfer (new link, fresh expiry, fresh download count), copies file metadata, **shares blobs** (no data copy) via `BlobRef`.
- FR-010-2: Pre-filled with the original's emails, password, and note; user can edit before sending.
- FR-010-3: Original transfer is marked `SupersededBy = newTransferId` (informational; both can be active).
- FR-010-4: Re-send of a transfer whose original blobs are already deleted (grace passed) → "Files were deleted, upload again" error.

**AC**

```gherkin
AC-010-1: Re-send an expired transfer within grace
  Then a new transfer is created, blobs shared (no duplication in storage)
  And the original is intact and still downloadable

AC-010-2: Re-send a transfer whose blobs are gone
  Then the error names the problem and no half-created transfer remains (draft cleanup)
```

**Technical notes**

- Storage model: `FileItem` references a `BlobRef` (logical content). Blob lifecycle is **reference-counted**: a blob is physically deleted only when no active transfer references it AND its own grace passed. This makes re-send cheap and is why deletion is a job, not a cascade.
- This is the single most important schema decision in MVP — implement `BlobRef` + `FileItem` (transfer↔blob junction) from day one.

---

### F-TRF-011 — Admin Dashboard (basic) — **P1**

*User story:* As the operator, I see what's live and who's on what plan.

**FR**

- FR-011-1: Roles: `Operator` (read) and `SuperAdmin` (full). Seeded manually.
- FR-011-2: Screens: **Overview** (active transfers, storage used, new users today, emails sent/failed 7d), **Transfers** (search by linkId/email/size/status, delete), **Users** (search, plan, storage used, force-delete), **Flags** (list & edit feature flags), **Email suppressions**.
- FR-011-3: Transfers search: by exact linkId, by recipient email, by status, by size range, date range.
- FR-011-4: Force-delete a transfer: same semantics as user delete.
- FR-011-5: Every admin mutation is audit-logged (Appendix C `admin.action` events) with actor email.

**AC**

```gherkin
AC-011-1: Operator opens Overview
  Then sees live counts within 5 min staleness (cached 5 min)

AC-011-2: SuperAdmin edits MAX_TRANSFER_SIZE flag
  Then new finalizes use the new value without deploy
  And the change is audit-logged
```

---

### F-TRF-012 — Telemetry & Observability — **P0** (foundation)

**FR**

- FR-012-1: Every feature emits the exact events in **Appendix C** — event names are part of the contract.
- FR-012-2: Correlation ID (W3C `traceparent`) on all requests, end-to-end (API → Service Bus → Functions).
- FR-012-3: Application Insights dashboards: **Upload funnel** (started→completed→failed), **Link funnel** (page_viewed→download_completed), **Job health** (expiries/deletions lag), **Email health** (sent/failed).
- FR-012-4: Alerts: upload success rate <99% over 15 min; expiry-job lag >30 min; email failure rate >5%.
- FR-012-5: No secrets or full file names in telemetry PII (file names hashed; email addresses stored in a separate PII property bag, marked).

---

### F-TRF-013 — Errors, 404s & Degraded States — **P0**

**FR**

- FR-013-1: Error screens: brand-consistent, one line + one action. No stack traces to users, all logged with correlation ID shown as "Ref: {id8}".
- FR-013-2: Global unhandled-error → 500 page with Ref ID; telemetry captured.
- FR-013-3: When Blob storage is down: upload fails with "Storage temporarily unavailable — retry"; downloads show "File temporarily unavailable."
- FR-013-4: When email is down: transfers still succeed (email is eventually-consistent); admin sees failure count.

**AC**

```gherkin
AC-013-1: Any 500
  Then the user sees "Ref: a1b2c3d4" and a retry button
  And the error is in Application Insights with that ref
```

---

### F-TRF-014 — i18n — **P1** (foundation: build i18n from day one)

**FR**

- FR-014-1: Languages: en (default), nl, fr, es, pt, it, de, tr.
- FR-014-2: UI strings 100% from translation files (no hardcoded literals — lint rule).
- FR-014-3: Locale detection: browser → account setting → default en.
- FR-014-4: Dates/numbers localized (`Intl`, ICU data).
- FR-014-5: Email templates localized per recipient's Accept-Language header (Communication Hub supports per-recipient attributes).

**AC**

```gherkin
AC-014-1: German browser, fresh visit
  Then entire UI is German

AC-014-2: Missing translation
  Then fall back to English and log a "translation_missing" event (never blank)
```

---

### F-TRF-015 — Dark Mode — **P1**

**FR**

- FR-015-1: Theme = system | light | dark (account setting; guests: system).
- FR-015-2: Full parity: every screen, including admin.
- FR-015-3: No flash of wrong theme: inline `data-theme` set before first paint.

---

### F-TRF-016 — Accessibility — **P1**

**FR**

- FR-016-1: WCAG 2.1 AA on all P0 surfaces (upload, recipient page, email CTA).
- FR-016-2: Keyboard: full flow doable without a mouse (upload surface has a visible "Choose files" button).
- FR-016-3: ARIA live regions for upload progress and password errors.
- FR-016-4: Focus states visible everywhere; color contrast ≥ 4.5:1.

---

### F-TRF-017 — Mobile Web — **P1**

**FR**

- FR-017-1: All P0 screens usable in mobile Safari/Chrome without desktop emulation.
- FR-017-2: Upload on mobile: uses `<input type=file>` (camera/Photos on iOS); no drag-and-drop required.
- FR-017-3: Download-all on iOS: triggers a visible download bar (Safari shows it for zip).
- FR-017-4: Touch targets ≥ 44 px.

---

## 3. Phase 1 — Pro / Monetization

> **Per-feature detail lives in `features/Phase 1-Pro/` (fully specced 2026-08-28):** `F-BIL-001-plan-catalog.md`, `F-BIL-002-stripe-lifecycle.md`, `F-BIL-003-plan-enforcement.md`, `F-PRF-001-from-branding.md`, `F-PRF-002-scheduled-send.md`, `F-PRF-003-download-analytics.md`, `F-PRF-004-ads-free-tier.md` — plus **one file per user story** (31 stories, US-018…024). **ID scheme:** Phase 1 continues the Phase 0 numbering per feature — F-BIL-001 → FR/AC/US-018, F-BIL-002 → 019, F-BIL-003 → 020, F-PRF-001 → 021, F-PRF-002 → 022, F-PRF-003 → 023, F-PRF-004 → 024. The entries below remain the canonical outline; the feature files restate and extend them. Phase 1 tasks: `Milestone-Backlog.md` M4.5 (T-031…040).

### F-BIL-001 — Plan Catalog — **P0 (for Phase 1)**

**FR**

- FR-001-1: Plans: **Free**, **Pro**, **Business** (names TBD). Plan defines all limit constants (Appendix A) + feature flags (ads on/off, analytics, branding, scheduling).
- FR-001-2: Plans are data (a `Plan` table), not code branches. Adding a plan = new row.
- FR-001-3: Plan prices: monthly/annual, per-seat for Business (seats = users).
- FR-001-4: Plan changes apply at next finalize (new transfers), not retroactively to active ones.

### F-BIL-002 — Stripe Subscription Lifecycle — **P0**

**FR**

- FR-002-1: Stripe Billing: Checkout for upgrade, Customer Portal for self-serve (cancel, update card).
- FR-002-2: Webhooks (must be idempotent by event ID):
  - `customer.subscription.created/updated` → plan + seats applied
  - `customer.subscription.paused` → **grace**: keep plan for 14 days, then downgrade to Free
  - `customer.subscription.resumed` → restore
  - `invoice.payment_failed` → 3 billing emails (0d, 3d, 7d), then pause
- FR-002-3: Upgrade = immediate; downgrade = end-of-period (Stripe default).
- FR-002-4: All billing state is a **mirror of Stripe** (we never trust our copy; Stripe is truth; mirror via webhooks).

**AC**

```gherkin
AC-002-1: Free user upgrades
  Then Stripe Checkout completes, plan applied within 60 s, "You're on Pro" banner

AC-002-2: Card fails
  Then 3 emails at 0/3/7 days, then subscription paused
  And after 14-day grace, plan downgrades to Free and a "You're back on Free" email is sent
```

### F-BIL-003 — Plan Enforcement — **P0**

**FR**

- FR-003-1: Single middleware resolves `User → Plan → Limits+Features` at request start; all features read from it (no scattered plan checks).
- FR-003-2: Over-quota active transfers on downgrade: allowed to finish their current life (they expire naturally); **new** sends blocked with "Upgrade to keep sending."
- FR-003-3: Plan screen: current plan, limits used vs. allowed (storage, active transfers), upgrade CTA.

### F-PRF-001 — Custom "From" Branding — **P1**

**FR**

- FR-001-1: Pro+: set an organization name + optional logo shown on recipient page header and in email "From:".
- FR-001-2: Per-user display name still available per-transfer.

### F-PRF-002 — Scheduled Send — **P1**

**FR**

- FR-002-1: Send "at {datetime}"; emails go out then; link active before (documented: "the link works early; emails go out on schedule").
- FR-002-2: Scheduled before 5 min / after 14 days → validation errors.

### F-PRF-003 — Download Analytics — **P1**

**FR**

- FR-003-1: Per transfer: downloads over time (daily buckets), countries (from first-download IP — privacy note), browsers.
- FR-003-2: Per-file download counts.
- FR-003-3: "Top downloads" list on the My Files detail page.

### F-PRF-004 — Ads (Free tier) — **P1**

**FR**

- FR-004-1: Free-tier recipient page shows one leaderboard ad below the file list (GPT slot); Pro pages: no ads.
- FR-004-2: Ad error → collapse gracefully (never breaks the page).

---

## 4. Phase 2 — Collect, Sign, Albums (outline level — expand before build)

### 4.1 Collect (many → one)

| ID | Feature | Key points |
|---|---|---|
| F-COL-001 | Create collection | Title, description, due date, optional per-field setup (name/email/file). URL `{origin}/collect/{id}`. Different entity from Transfer — do not reuse. |
| F-COL-002 | Submission | Contributor (no account) uploads to a collection; multiple submissions per person allowed; each submission gets its own status. |
| F-COL-003 | Submissions dashboard | Collector sees all submissions: person, files, status (received/accepted/declined), download all per person. |
| F-COL-004 | Due date | Past due → banner; submissions still accepted `DUE_GRACE_DAYS` after. |
| F-COL-005 | Completion | "Mark done" per submission; email notification to contributor. |

**Schema note:** `Collection`, `CollectionEntry` (per-sender submission), reuses `FileItem`/`BlobRef`.

### 4.2 Sign

| ID | Feature | Key points |
|---|---|---|
| F-SGN-001 | Send document | PDF (or docx→pdf), per-recipient sign order, email. |
| F-SGN-002 | Sign | Draw/type/upload signature, date stamp, `AgreedOn` metadata. |
| F-SGN-003 | Audit trail | Immutable log: viewed, signed, IP, timestamp. (GDPR: state this clearly.) |
| F-SGN-004 | Final doc | Merged signed PDF generated async; downloadable by all parties. |

### 4.3 Albums

| ID | Feature | Key points |
|---|---|---|
| F-ALB-001 | Create album | Title, cover, optional password, max size. |
| F-ALB-002 | Share | Link + email invite; persistent (doesn't expire on a 7-day clock). |
| F-ALB-003 | Contribute | Owner + optionally invited guests upload; grid view, lightbox. |
| F-ALB-004 | Mobile view | Swipeable grid, full-bleed image view, download. |

---

## 5. Phase 3 — Enterprise

> **Per-feature detail lives in `features/Phase 3-Enterprise/` (fully specced 2026-08-28):** `F-ENT-001-sso.md`, `F-ENT-002-scim.md`, `F-ENT-003-org-admin.md`, `F-ENT-004-data-residency.md`, `F-ENT-005-custom-subdomain.md`, `F-ENT-006-workspaces.md` — plus **one file per user story** (27 stories, US-038…043). **ID scheme:** F-ENT-001 → FR/AC/US-038 … F-ENT-006 → 043. Phase 3 adds the **org layer** (`Organization`, `OrgMember`, `OrgAuditEntry` — DDL in F-ENT-003; `IdpConfig` in F-ENT-001; `ScimGroup` in F-ENT-002; `StorageAccount`/`ResidencyAnomaly` in F-ENT-004; `OrgDomain` in F-ENT-005; `Workspace` in F-ENT-006). M6 tasks: T-056…064 in `Milestone-Backlog.md`. The entries below remain the canonical outline; the feature files restate and extend them.


| ID | Feature | Key points |
|---|---|---|
| F-ENT-001 | SSO | Entra ID OIDC (and SAML) at org level. Per-tenant IdP config. |
| F-ENT-002 | SCIM | User/group provisioning from Entra. Map `externalId` on `User`. |
| F-ENT-003 | Admin & audit | Org admin: members, groups (limits per group), full audit log (who did what, when, IP), CSV export. |
| F-ENT-004 | Data residency | Choose region at org creation (EU / US); all blobs + SQL in that region; flag on org. |
| F-ENT-005 | Custom subdomain | `transfers.{yourorg}.com` — CNAME to Front Door, TLS via cert automation. |
| F-ENT-006 | Workspaces | Groups within an org, per-group plan + limits. |

---

## 6. Cross-Cutting (sprinkle across all phases)

> **Full specs + user stories live in `features/` (F-XCT-001…005; stories US-044…048).** The entries below are the canonical summaries.

### F-XCT-001 — Feature Flags & Remote Limits — **P0 (foundation)**

All plan limits + feature gates (ads, analytics, scheduling, search, data export, email settings) live in the flag service (`FeatureFlag` table, TA-13.2); admin edits without a deploy via `GET/PUT /api/v1/admin/flags` (endpoint 23); `FlagsCache` 30 s TTL (ADR-008); audit-logged (`admin_action`). Keys are seeded and finite: `limits.<plan>.*`, `feature.ads`, `feature.scheduling`, `feature.analytics`, `feature.resend`, **plus `feature.search`, `feature.dataExport`, `feature.emailSettings`** (added by F-XCT-001). Absent key = default (feature off). Spec: `features/F-XCT-001-feature-flags.md`.

### F-XCT-002 — Search (Transfers) — **P1**

Full-text search on transfers by **file name, recipient email, sender name**: SQL `CONTAINS` on a full-text-indexed `SearchText` column first, Azure Search only if needed (then an ADR, endpoint unchanged). My Files `?q=` (owner-scoped) + admin transfer list; gated by `feature.search`. Spec: `features/F-XCT-002-search.md`.

### F-XCT-003 — Email Settings (Notification Preferences) — **P1**

Per-account switch "Email me about my transfers and accounts" (`AppUser.EmailNotificationsEnabled`, NULL = on). Checked **at send time** by `f-email`; suppresses *owner* notifications only — recipients and billing are exempt. Independent of per-sender unsubscriptions (F-TRF-006). Digest = P2. Gated by `feature.emailSettings`. Spec: `features/F-XCT-003-email-settings.md`.

### F-XCT-004 — GDPR: Data Export & Erasure Completeness — **P1**

Account delete = F-TRF-008-8 (unchanged). Adds: **data export** (zip of `profile.json` + `transfers.json` metadata, 24-h SAS, 7-day lifecycle; `feature.dataExport` gate; `EXPORT_IN_FLIGHT` 409) and **erasure completeness** (`account_deleted` is the erasure marker; nightly `f-gdpr-sweep` scrubs App Insights PII inside the 90-day retention; admin **Erasures** screen with sweep status). Spec: `features/F-XCT-004-gdpr-export-erasure.md`.

### F-XCT-005 — PII Policy — **P1 (enforced from day one)**

Canonical, normative list: PII = email addresses, sender names, file names, IPs. Raw PII only in the closed list of columns (TA-9.4); telemetry: file names SHA-256, IPs HMAC(`Wa:Jwt:Secret`), raw emails only under the `Pii=true` property. Enforced by `[Pii]` marker + convention tests + CI grep (build gate, not review convention); erasure cascade (F-XCT-004) must cover every marked column. Spec: `features/F-XCT-005-pii-policy.md`.

*(i18n, dark mode, accessibility, and mobile-web are cross-cutting too but are specced in Phase 0 as F-TRF-014…017.)*

---

## Appendix A — Limit Constants (single source of truth)

| Constant | Free | Pro | Business |
|---|---|---|---|
| `MAX_TRANSFER_SIZE` | 5 GB **TBD** | 20 / 50 GB **TBD** | 100 GB |
| `MAX_SINGLE_FILE` | same as transfer | same | same |
| `RETENTION_DAYS` | 7 **TBD** | 30 **TBD** | 90 |
| `GRACE_DAYS` | 3 | 3 | 3 |
| `MAX_DOWNLOADS` | 100 | 1000 | ∞ |
| `MAX_EMAILS` | 20 | 100 | 500 |
| `STORAGE_QUOTA` | 5 GB | 100 GB | 1 TB |
| `ACTIVE_TRANSFERS_MAX` | 20 | 200 | ∞ |
| `SCHEDULING` | off | on | on |
| `BRANDING` | off | on | on |
| `ANALYTICS` | off | on | on |
| `ADS` | on | off | off |
| `SSO/SCIM` | off | off | on |

**Rule:** code reads these from configuration, never literal values.

## Appendix B — Core Entities (MVP)

- `User` (Id, Email, DisplayName, PlanId, StorageBytes, CreatedAt, ExternalId?)
- `Plan` (Id, Name, LimitsJson, FeaturesJson, PriceJson)
- `Transfer` (Id, LinkId, OwnerUserId?, Status, ExpiresAt, MaxDownloads, DownloadsCount, PasswordHash?, SenderName, SenderEmail?, Note?, TotalBytes, SupersededBy?, CreatedAt, ExpiredAt?, DeletedAt?)
- `FileItem` (Id, TransferId, BlobRefId, OriginalName, SizeBytes, ContentType, SortOrder)
- `BlobRef` (Id, BlobPath, SizeBytes, RefCount, CreatedAt, PhysicallyDeletedAt?) — **the join that makes re-send free**
- `EmailRecipient` (Id, TransferId, Address, NotifiedAt)
- `EmailSuppression` (Id, Address, SenderEmail, CreatedAt)
- `Collection` / `CollectionEntry` (Phase 2)
- `Document` / `Signature` / `AuditEntry` (Phase 2)
- `Album` / `AlbumItem` (Phase 2)
- `Organization` / `OrgMember` / `OrgAuditEntry` (Phase 3)

## Appendix C — Event Contract (Service Bus topic `core`)

| Event | Payload keys | Emitter |
|---|---|---|
| `transfer.created` | transferId, linkId, totalBytes, emails[] | API (send) |
| `transfer.expired` | transferId | expiry job |
| `transfer.deleted` | transferId, reason | deletion job |
| `email.sent` / `email.failed` | transferId, to, attempt | email worker |
| `download.completed` | transferId, fileId, ip, ua | API |
| `zip.generated` | transferId, size, duration | zip function |
| `user.created` / `account.deleted` | userId | API |
| `plan.changed` | userId, fromPlan, toPlan | billing webhook |
| `admin.action` | actor, entity, entityId, action | admin API |

## Appendix D — Glossary

- **Transfer** — one logical send: files + link + recipients + expiry
- **Draft** — finalized blobs not yet "sent" (no link, no emails)
- **BlobRef** — physical storage item, shared across transfers (ref-counted)
- **Re-send** — new transfer sharing the old transfer's BlobRefs
- **Growth loop** — recipient → "Send something" → new sender
- **Grace period** — days after expiry before physical deletion (re-send window)

---

**Suggested implementation order (MVP):** F-TRF-012 (telemetry foundation) → F-TRF-001/002 (upload+link) → F-TRF-003/004 (download) → F-TRF-005 (expiry) → F-TRF-006 (email) → F-TRF-007 (limits) → F-TRF-008/009/010 (accounts, history, re-send) → F-TRF-011 (admin) → i18n/dark/a11y/mobile pass → Phase 1 billing.
