# T-008-04 — Magic Link: Request + Redeem (FR-008-1)

**Task:** `T-008-04` | **Spec row:** FR-008-1/3, AC-008-2, EC-008-3
**Story:** US-008-02-magic-link
**Milestone:** T-021 part 4 (magic link request + redeem)
**Branch:** `feature/F-TRF-008`
**Size:** M

---

## DESIGN OVERVIEW

Two MediatR use cases mirroring the existing auth-command pattern (`SignupCommand`/`LoginCommand`): **RequestMagicLink** creates a hashed single-use token and enqueues the f-email worker; **RedeemMagicLink** verifies it, signs in (cookie via T-008-02 middleware), returns profile DTO. No ASP.NET Identity tables (ADR-007); `/auth/*` already rate-limited 10/min per IP (TA-9.5).

### D-1: RequestMagicLink — token generation + event publish

Generate a 256-bit random token (`RandomNumberGenerator.GetBytes(32)`), store its SHA-256 hex (64 chars) as `AuthToken.TokenHash` with `Purpose=1`, `ExpiresAtUtc = now + 10 min`. Publish an `auth.magic_link_requested` event via `IEventPublisher` (outbox pattern, same as SendTransfer publishes `transfer.created`). The raw token goes in the event payload (only the hash is persisted — TA-9.1: DB leak reveals no working link).

**Citations:**
- `AuthToken` entity fields Id/Email/TokenHash/Purpose/ExpiresAtUtc/RedeemedAtUtc/CreatedAtUtc (`src/wa.domain/AuthToken.cs`)
- `AuthTokenConfiguration.cs` TokenHash CHAR(64) + UQ_AuthToken_Hash unique index
- SendTransferHandler event publish pattern (`src/wa.application/UseCases/Transfers/SendTransfer.cs` §7–9)

### D-2: RedeemMagicLink — lookup + sign-in + create-on-redeem

Look up AuthToken by SHA-256(token); validate not expired AND `RedeemedAtUtc` is null (single-use, set it). Unknown email → create AppUser on redeem (sign-up is sign-in, FR-008-3) with `user_created` emitted BEFORE `login_success`. On success mint session token via ISessionTokenMinter and return profile DTO.

**Citations:**
- IAppUserStore.FindByEmailAsync + CreateAsync (`src/wa.application/UseCases/Auth/IAppUserStore.cs`)
- TelemetryEvents.UserCreated / LoginSuccess (`src/wa.domain/Telemetry/TelemetryEvents.cs`)
- SignupHandler sign-up flow (create then login) as reference

### D-3: Expired ≡ Redeemed — indistinguishable state (EC-008-3)

Both expired (>10 min) and redeemed tokens return the identical `LinkExpired` error (410 Gone). Unknown hash also maps to same code (don't leak which emails/links exist). Token row stays in AuthToken until swept (sweep job out of scope here).

### D-4: Session cookie via T-008-02 middleware

On redeem success, set the `wa.session` cookie from ISessionTokenMinter result (same mechanism as auth/login MapPost) and return 200 with profile DTO + session token in JSON body. Emit `login_success` telemetry.

### D-5: Endpoint wiring — MediatR ISender + validation catch

Both endpoints wired in Program.cs via `app.MapPost` using MediatR ISender (not raw DI), matching auth/signup and auth/login exactly. Validation errors → `BusinessException(ProblemCodes.Validation)` per T-056 pipeline; link-expired → `BusinessException(ProblemCodes.LinkExpired)`.

### D-6: f-email routing — new event type + dedicated handler

`EmailFunction.Run` (wa.workers) updated to dispatch on EventType:
- `TransferCreated` → existing `SendNotificationEmailsHandler` (unchanged)
- `MagicLinkRequested` → **new** `SendMagicLinkEmailHandler` (self-contained, no DB context needed — payload carries email + token + expiry)

New `SendMagicLinkEmailHandler` in `wa.application/UseCases/Auth/`: loads magic-link template from `AppContext.BaseDirectory/Emails/templates/magic-link/en.{html,txt}`, renders via StaticStubbleRenderer (same Stubble package as existing renderer), sends via IEmailSender. Self-contained — no changes to ITemplateRenderer or TemplateData (which are transfer-shaped).

**Citations:**
- EmailFunction.Run filter: `envelope.Type != EventType.TransferCreated` (`src/wa.workers/EmailFunction.cs`)
- StubbleTemplateRenderer pattern + StaticStubbleRenderer usage (`src/wa.infrastructure/Email/StubbleTemplateRenderer.cs`)
- workers csproj `<Content Include="Emails\templates\**\*" CopyToOutputDirectory="PreserveNewest" />`

### D-7: Error code — LinkExpired (410 Gone) — 18th ProblemCodes member

Add `LinkExpired = "LINK_EXPIRED"` to the closed list with status 410, title "This link has expired". Deliberate extension of the golden-rule 17-member list for F-TRF-008 (justification: EC-008-3 requires a distinct code separate from VALIDATION/WRONG_PASSWORD; TA-9.1 single-use token expiry is semantically 410 Gone like TransferExpired). Update comment to "exactly these 18 members".

**Citations:**
- ProblemCodes.cs golden-rule comment + existing TransferExpired=410 precedent
- EC-008-3 (expired ≡ redeemed, don't leak which)

---

## FILES FOR CODER

| # | Path | Action | What to change |
|---|------|--------|---------------|
| 1 | `src/wa.application/UseCases/Auth/IAuthTokenStore.cs` | **create** | Port interface: `FindByHashAsync(string sha256hex, ct)` → AuthToken?; `CreateAsync(AuthToken token, ct)`; `MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, ct)`. Follow IAppUserStore pattern (thin adapter over WaDbContext). |
| 2 | `src/wa.infrastructure/Persistence/AuthTokenStore.cs` | **create** | EF implementation of IAuthTokenStore. Singleton over adapter-owned WaDbContext. Same style as AppUserStore (T-008-01). |
| 3 | `src/wa.application/UseCases/Auth/RequestMagicLink.cs` | **create** | `sealed record RequestMagicLinkCommand(string Email) : IRequest<RequestMagicLinkResult>`; handler: validate email format → generate 256-bit random token → persist AuthToken{Email, TokenHash=sha256hex(token), Purpose=1, ExpiresAtUtc=now+10min} via IAuthTokenStore → publish event (IEventPublisher) with payload {email, token(raw base64), expiresAtUtc} → return 202 result. Follow Signup.cs layout (record + handler + ctor DI). |
| 4 | `src/wa.application/UseCases/Auth/RedeemMagicLink.cs` | **create** | `sealed record RedeemMagicLinkCommand(string Token) : IRequest<RedeemMagicLinkResult>`; handler: sha256(token hex/base64decode) → IAuthTokenStore.FindByHashAsync → validate (not null, not redeemed, not expired) → set RedeemedAtUtc (first-redeem-wins) → FindByEmailAsync on email from AuthToken row → if null create AppUser (same fields as SignupHandler: DisplayName=local part, Theme="system", PlanId=free plan, EmailConfirmed=true — magic link IS the confirmation) + emit user_created BEFORE login_success → mint session token via ISessionTokenMinter.Mint(user.Id) → return LoginResult-shaped DTO (UserId, Email, DisplayName, Theme, PlanCode, SessionToken). Expired/redeemed/unknown → throw LinkExpiredException (maps to 410 at endpoint). |
| 5 | `src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs` | **create** | Self-contained f-email handler: DI ctor(IEmailSender, WaTelemetryReporter, ILogger); method `HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)`. Extract email+token+expiresAtUtc from payload; build CTA URL = `{config["Wa:Url:Public"]}/auth?ml={raw_token}` (publicOrigin default "http://localhost:5173"); load template from `AppContext.BaseDirectory/Emails/templates/magic-link/en.html` + `.txt`; render via StaticStubbleRenderer with model {Email, CtaUrl}; send EmailMessage via IEmailSender; emit email.sent event via IEventPublisher (same pattern as SendNotificationEmailsHandler §8); on failure at maxDeliveryCount → emit email.failed + telemetry. |
| 6 | `src/wa.domain/EventEnvelope.cs` | **modify** | Add EventType member: `[JsonPropertyName("auth.magic_link_requested")] MagicLinkRequested` (12th in closed list — architecture extension per EC-008-3 + TA-9.1). Update comment to reflect 12 members at M0. |
| 7 | `src/wa.workers/EmailFunction.cs` | **modify** | Change filter from `envelope.Type != EventType.TransferCreated` to dispatch: if TransferCreated → existing handler; else if MagicLinkRequested → new SendMagicLinkEmailHandler.HandleAsync(...). Add DI for the new handler. |
| 8 | `src/wa.workers/Program.cs` | **modify** | Register SendMagicLinkEmailHandler singleton (DI wiring alongside existing IEmailStore/ITemplateRenderer/IEmailSender registrations). |
| 9 | `src/wa.workers/Emails/templates/magic-link/en.html` | **create** | HTML email template: subject "Your ProtoDrop sign-in link", body with CTA button `{CtaUrl}`, dark-mode-safe (600 px, calm tone per T-006-05), expiry note. Stubble variables: `{{Email}}`, `{{CtaUrl}}`. |
| 10 | `src/wa.workers/Emails/templates/magic-link/en.txt` | **create** | Plain-text equivalent of the HTML template (same CTA URL, no markup). |
| 11 | `src/wa.domain/Errors/ProblemCodes.cs` | **modify** | Add `public const string LinkExpired = "LINK_EXPIRED";` + table entry `[LinkExpired] = (410, "This link has expired")`. Update comment: "exactly these 18 members". |
| 12 | `src/wa.api/Program.cs` | **modify** | Add DI registration for IAuthTokenStore. Add two MapPost routes (see STEP details below). |
| 13 | `tests/wa.application.unit/Auth/RequestMagicLinkTests.cs` | **create** | xunit + hand-stubs (no Moq in repo): token stored as SHA-256 hex with Purpose=1 and 10-min TTL; event published with correct payload shape (email, raw token); invalid email → validation exception. |
| 14 | `tests/wa.application.unit/Auth/RedeemMagicLinkTests.cs` | **create** | xunit + hand-stubs: first redeem signs in (profile DTO + session token minted, login_success emitted); second redeem of same hash → LinkExpiredException; expired (>10 min) and redeemed return identical exception (EC-008-3); new email → AppUser created with user_created before login_success. |
| 15 | `docs/Open-Decisions-and-Constants.md` | **modify** | Record magic-link constants in Part 2 if not already listed: token TTL = 10 min, Purpose values (0=signup, 1=magic-link-login, 2=forgot-password). Keep existing doc format. |

---

## ORDERED STEPS

1. **CodeGraph auto-compaction** → then explore: IAppUserStore + AppUserStore EF impl; Program.cs auth route block (MapPost patterns for signup/login); AuthToken entity + AuthTokenConfiguration; EmailFunction.Run + SendNotificationEmailsHandler.HandleAsync signature; StubbleTemplateRenderer StaticStubbleRenderer usage; TelemetryEvents auth event names; Signup.cs full (SessionTokenMinter/ProfileDto shape).

2. **Create IAuthTokenStore.cs** (port interface) — 3 methods: FindByHashAsync, CreateAsync, MarkRedeemedAsync.

3. **Create AuthTokenStore.cs** (EF implementation in wa.infrastructure.Persistence) — singleton over adapter-owned WaDbContext, same style as AppUserStore.

4. **Add EventType.MagicLinkRequested** to EventEnvelope.cs (12th member, wire name "auth.magic_link_requested").

5. **Add ProblemCodes.LinkExpired** (410, "This link has expired") + update golden-rule comment to 18 members.

6. **Create RequestMagicLink.cs** — record + handler per D-1. Dependencies: IAuthTokenStore, IEventPublisher, IConfiguration (for no external config needed yet). Returns a result record with token value for the event payload (or publishes internally and returns void/202 marker).

7. **Create RedeemMagicLink.cs** — record + handler per D-2/D-3/D-4. Dependencies: IAuthTokenStore, IAppUserStore, IPlansStore, ISessionTokenMinter, WaTelemetryReporter. Throws LinkExpiredException (sealed class with factory, same pattern as LoginWrongPasswordException).

8. **Create SendMagicLinkEmailHandler.cs** — self-contained f-email handler per D-6. Dependencies: IEmailSender, IEventPublisher, IConfiguration, WaTelemetryReporter, ILogger. Loads template from magic-link directory; renders via StaticStubbleRenderer (using Stubble.Core); sends + emits events.

9. **Modify EmailFunction.Run** in wa.workers — dispatch on event type to route MagicLinkRequested → SendMagicLinkEmailHandler. Update DI.

10. **Modify workers/Program.cs** — register SendMagicLinkEmailHandler singleton.

11. **Create template files**: `src/wa.workers/Emails/templates/magic-link/en.html` + `en.txt`.

12. **Wire MapPost routes in Program.cs** (API):
    - `POST /api/v1/auth/magic-link`: body `{email}` → ISender.Send(RequestMagicLinkCommand) → 202 Accepted (empty body or minimal JSON). Catch validation → BusinessException(Validation).
    - `POST /api/v1/auth/magic-link/redeem`: body `{token}` → ISender.Send(RedeemMagicLinkCommand) → set wa.session cookie from result.SessionToken + return Results.Json(profile DTO, statusCode: 200). Catch LinkExpiredException → BusinessException(LinkExpired); catch validation → BusinessException(Validation).

13. **Add unit tests**: RequestMagicLinkTests.cs + RedeemMagicLinkTests.cs (hand-stub ports — anonymous objects implementing IAppUserStore/IAuthTokenStore/ISessionTokenMinter, same pattern as LoginHandlerTests).

14. **Update docs/Open-Decisions-and-Constants.md** with magic-link constants if not already listed.

15. **Run test gate** per AGENT.md §4:
    - `dotnet test tests/wa.domain.unit`
    - `dotnet test tests/wa.application.unit`
    - `dotnet build tests/wa.api.integration -v quiet` (compile check only)
    Fix fallout until all green.

16. **Commit**: `feat: T-008-04 magic-link endpoint request + redeem single-use token`

---

## ACCEPTANCE CRITERIA (exit check)

- [ ] POST /api/v1/auth/magic-link with a known email returns 202 and the f-email worker sends an email containing a working link (dev log-sender visible; template rendered correctly).
- [ ] First click of the link signs in: wa.session cookie set, profile DTO returned, login_success emitted. Second click of same link → "This link has expired." + 410 Gone (AC-008-2, single-use).
- [ ] Link clicked after 10 min returns an identical state to a redeemed one — no distinction leaked (EC-008-3).
- [ ] New email + magic link → AppUser created on redeem with user_created emitted BEFORE login_success.
- [ ] On success the session cookie is set via T-008-02 middleware and telemetry login_success is emitted exactly once.

---

## ERROR CASES

| Scenario | Code / Status | Notes |
|----------|--------------|-------|
| Unknown hash, redeemed, or expired token on redeem | LINK_EXPIRED (410) | Same code for all three — don't leak which (EC-008-3). Single-use by design. |
| Malformed/missing email in request body | VALIDATION (400) | Problem+JSON via T-056 BusinessException pipeline, matching existing auth routes. |
| Malformed/missing token in redeem body | LINK_EXPIRED (410) or VALIDATION (400)? → **VALIDATION (400)** — malformed input is a client error; no-row-found is the expired case. | Keep distinction: bad format ≠ unknown hash. |
| Rate limit exceeded | RATE_LIMITED (429) | /auth/* already 10/min per IP (TA-9.5) — no new limiter needed. |

---

## CONVENTIONS

- .NET 10; C# record commands, one use-case file per flow under `src/wa.application/UseCases/Auth/`.
- MediatR IRequest/IRequestHandler + ISender in Program.cs MapPost (same as Signup/Login).
- xunit + **hand-stubs** for unit tests (no Moq anywhere in repo — grep confirms zero hits across all test csproj files). Pattern: anonymous objects implementing port interfaces.
- Problem+JSON via ProblemCodes for all error responses; no ad-hoc status codes.
- Constants tracked in docs/Open-Decisions-and-Constants.md Part 2 FROZEN sections only (TTL=10 min, Purpose values from TA-3.2).
- No new packages: Stubble.Core already referenced by wa.infrastructure; RandomNumberGenerator is BCL; SHA256 is BCL.

---

## ASSUMPTIONS / OUT OF SCOPE

- **Client-side `?ml=` param handling**: AuthPage.tsx (committed in T-008-03) does NOT yet parse the `?ml=` query parameter or POST to `/auth/magic-link/redeem`. That client work is deferred to a follow-up task (T-008-05 or later). This task delivers the API endpoints; the ghost button in AuthPage.tsx already POSTs to `/auth/magic-link` for request.
- **AuthToken sweep job**: expired tokens remain in DB until a future cleanup task handles them (out of scope here).
- **Full Testcontainers integration suite** deferred to feature end per Definition of Done — this gate is unit tests + compile check only.
- **Multiple tabs race**: two concurrent redeems of same token — first EF write wins, second gets expired state on re-read (acceptable for MVP; no optimistic concurrency token on AuthToken).

---

## DEPENDENCIES (already in place)

- T-008-02: SessionMiddleware + ISessionTokenMinter (cookie format TA-9.1, 30-day rolling)
- T-054: IEventPublisher + outbox pattern (SbEventPublisher / InMemoryEventPublisher)
- T-006: f-email consumer infrastructure (IEmailSender, LogEmailSender dev switch, Stubble.Core package)
- AuthToken EF table + configuration from prior migrations (no new migration needed)
