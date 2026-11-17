# T-008-05 — ForgotPasswordCommand + endpoint 11: anti-enumeration link (FR-008-6)

**Task:** `T-008-05` | **Spec row:** FR-008-6, TA-4.2#11
**Story:** US-008-03-forgot-password
**Milestone:** T-021 part 5 (forgot-password request endpoint; redeem reuses endpoint 10 as-is)
**Branch:** `feature/F-TRF-008`
**Size:** S

---

## DESIGN OVERVIEW

One new MediatR use case mirroring T-008-04's RequestMagicLink **exactly**, plus one self-contained f-email handler mirroring SendMagicLinkEmailHandler. The anti-enumeration twist is the whole delta: for an unregistered email the handler does NO row insert and NO event publish — yet returns the same result record, so the endpoint emits a byte-identical `202 Accepted` (no body) either way. Redeem needs zero changes: verified in `RedeemMagicLink.cs` that lookups filter on `TokenHash` only — no `Purpose` check anywhere in the redeem path, so a Purpose=2 row signs in exactly like Purpose=1 (single-use + expiry inherited for free).

**Pre-existing build defect to fix en route:** `src/wa.workers/EmailFunction.cs:47` does not compile today — `var handler = envelope.Type switch { ... }` over two unrelated sealed classes yields **CS8506 "No best type was found for the switch expression"** (`dotnet build src/wa.workers` fails, verified). T-008-04's dispatch arm is latent-broken. This task adds a third routing case and MUST introduce a shared interface to give the switch a common type — see D-3.

### D-1: ForgotPasswordCommand handler — existence gate BEFORE token insert (anti-enumeration)

Order of operations inside `ForgotPasswordHandler.Handle`:
1. Normalize email (lowercase + trim, same `NormalizeEmail` form as RequestMagicLink/Signup) → validate with MailAddress (same helper shape) → invalid → `ForgotPasswordValidationException` → 400 VALIDATION at endpoint.
2. **Check registration first**: `_users.FindByEmailAsync(email)` — this is the difference from RequestMagicLink, which always inserts. If null → skip token + event entirely and return the same result (unregistered path).
3. Registered: generate `RandomNumberGenerator.GetBytes(32)` base64 raw token; store SHA-256 hex as `AuthToken.TokenHash` with **`Purpose=2`** (`private const byte PurposeForgotPassword = 2` — value from the registry row "Magic link AuthToken Purpose values | 0=signup, 1=magic-link-login, 2=forgot-password" in Open-Decisions-and-Constants.md Part 2), `ExpiresAtUtc = now + RequestMagicLinkHandler.TtlSeconds` (the frozen **600 s** constant — verified to live at `src/wa.application/UseCases/Auth/RequestMagicLink.cs` line 22, same assembly; reference it, do NOT re-declare a new number anywhere).
4. Publish `auth.magic_link_requested` via `IEventPublisher` with payload `{ email, token (raw base64), expiresAtUtc, purpose = 2 }`. The extra **`purpose` int field is the D-3 dispatch discriminator** — read by EmailFunction only; both email handlers parse payloads by named properties and ignore unknown fields (verified in SendMagicLinkEmailHandler.HandleAsync: `TryGetProperty("email"/"token"/"expiresAtUtc")`).
5. Return `ForgotPasswordResult()` — a 202 marker record with **no fields** (same as RequestMagicLinkResult). Byte-identity of the wire body follows from: endpoint returns `Results.Accepted()` for both paths; unregistered never reaches a different branch.

**Citations:**
- `RequestMagicLink.cs` full (src/wa.application/UseCases/Auth/) — pattern to mirror, including correlation-guid derivation + ComputeSha256Hex + NormalizeEmail/IsValidEmail helpers (copy the same private statics; no shared-base-class refactor in this task).
- `IAppUserStore.FindByEmailAsync(string lowercasedEmail, ct)` (src/wa.application/UseCases/Auth/IAppUserStore.cs) — existence check.
- TA-4.2#11 (emits: "metrics"), FR-008-6, US-008-03 Alternative flows ("Unregistered email: same 'Link sent' state; no row, no send").

### D-2: Event-type fork — REUSE `auth.magic_link_requested`, do NOT add a 13th member

Fork resolved. Options considered:
- **(a) Add 13th EventType member** (`auth.forgot_password_requested`): semantically pure, but expands the TA-5.3 closed list AGAIN (another architecture extension after T-008-04's D-6), requires modifying `src/wa.domain/EventEnvelope.cs`, updating `tests/wa.domain.unit/Events/EnvelopeShapeTests.cs` (the pinned 12-member list + exact-string pass per type), and a new dispatch arm in EmailFunction.
- **(b) Reuse the T-008-04 event** (chosen): same payload shape, same CTA URL (`{publicOrigin}/auth?ml={token}` — the frontend link-screen treats both purposes identically; post-redeem UI copy already says "signed in"), and the subject/template difference lives entirely inside the f-email worker layer.

Decision: **(b)** — but with a dedicated handler rather than overloading SendMagicLinkEmailHandler (see D-3). Rationale cited: TA-5.3 closed list ("no ad-hoc types at M0" — reusing an existing type is not an extension); EnvelopeShapeTests AC-054-2 stays green untouched; wire contract for `auth.magic_link_requested` unchanged (same payload fields, same key names).

**Citations:**
- `EventEnvelope.cs` EventType enum — exactly 12 members at T-008-05 start.
- `EnvelopeShapeTests.EventEnvelope_SerializesToExactTa52Shape_ForEveryTa53Type` — pins the list; untouched in this task.

### D-3: New SendForgotPasswordEmailHandler + EmailFunction dispatch rework (fixes the CS8506 pre-existing defect)

**Handler:** create `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs` — a copy of SendMagicLinkEmailHandler with exactly three differences, all grounded in verified source:
1. Subject const: **`"Your ProtoDrop link"`** (FR-008-6) vs magic-link's "Your ProtoDrop sign-in link" (verified: subject is a private const at SendMagicLinkEmailHandler.cs line 18 — NOT config-driven and NOT payload-driven, so a separate handler is what makes the subject swap clean; no config change).
2. Template dir: `AppContext.BaseDirectory/Emails/templates/forgot-password/` (mirrors line 40) — en.html + en.txt with forgot-password copy ("Forgot? We'll sign you in — set a new password from your profile afterwards" — documents "forgot ≠ reset" per US-008-03 edge case), same variables `{{Email}}`, `{{CtaUrl}}`, `{{MinutesRemaining}}` (same manual substitution as lines 61–65).
3. email.sent / email.failed payloads: `kind = "forgot_password"` (mirrors existing `kind = "magic_link"` at lines 119/148).

Everything else identical — same ctor `(IEmailSender, IEventPublisher, IConfiguration, WaTelemetryReporter, ILogger<T>)`, same HandleAsync signature `(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)` (the last three params are what EmailFunction passes at line 59, so the signature must match for dispatch to work), same CTA URL construction (`{publicOrigin}/auth?ml={rawToken}`, line 83), same email.sent emit + email.failed/DLQ telemetry on final attempt (mirrors lines 131–154).

**Dispatch — three changes (fixes the CS8506 while adding routing):**
The current switch at EmailFunction.cs line 47 does not compile as written (CS8506 — two arms yield unrelated sealed classes, `_ => null` gives no common type; verified by building). T-008-05 must fix this while adding the third routing case:
1. Create a shared interface in wa.application (keeps handlers SB-free — they already are): `src/wa.application/UseCases/Auth/IEmailEventHandler.cs`:
   ```csharp
   public interface IEmailEventHandler { Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct); }
   ```
2. One-line base-list additions on **all three** email handlers (SendNotificationEmailsHandler, SendMagicLinkEmailHandler, SendForgotPasswordEmailHandler) — their HandleAsync signatures already match exactly (verified), zero body changes.
3. Rewrite the EmailFunction switch with an explicit type + a payload discriminator:
   ```csharp
   var handler: IEmailEventHandler? = envelope.Type switch
   {
       EventType.TransferCreated => _notificationHandler,
       EventType.MagicLinkRequested => IsForgotPasswordPayload(envelope) ? _forgotPasswordHandler : _magicLinkHandler,
       _ => null
   };
   ```
   with a private static helper:
   ```csharp
   private static bool IsForgotPasswordPayload(EventEnvelope envelope)
   {
       var json = JsonSerializer.Serialize(envelope.Payload);
       using var doc = JsonDocument.Parse(json);
       return doc.RootElement.TryGetProperty("purpose", out var p) && p.ValueKind == JsonValueKind.Number && p.GetInt32() == 2;
   }
   ```
   (Serializing-then-parsing mirrors what SendMagicLinkEmailHandler itself does at line 55 — same idiom as the codebase, no new abstraction.)

**Citations:**
- `SendMagicLinkEmailHandler.cs` full (verified: subject const line 18, template dir line 40, payload parsing lines 59–78 read-by-name with unknown-field safety, CTA URL line 83, email.sent/failed event emission + DLQ telemetry — all to mirror).
- `EmailFunction.cs` full (verified: dispatch at lines 47–52; CS8506 error confirmed by building).
- workers csproj `<Content Include="Emails\templates\**\*" CopyToOutputDirectory="PreserveNewest" />` already covers the new template dir — no csproj change.

### D-4: Telemetry — request-volume metric = one new reporter method (metric addition, NOT event addition)

TA-4.2#11's "Emits" column says **metrics** (not a TA-10.2 event name). The closed set is `TelemetryEvents` with 23 events + 7 metrics; `LogEvent(name)` throws for any name outside the set, and the 7 metric members are all emitted through dedicated reporter methods (UploadCompleted, ZipGenerated, ExpiryJobLag, EmailFailures1h, StorageBytesActive, TransferPageViewed — verified in WaTelemetryReporter.cs). A plain inline counter is NOT the grounded surface; **reporter methods** are.

Decision: add one method to `WaTelemetryReporter`:
```csharp
/// <summary>TA-4.2#11 abuse signal: forgot-password request volume (FR-008-6).</summary>
public void ForgotPasswordRequested() =>
    _logger.LogInformation("forgot_password_requested {count}", 1);
```
— exact shape of the existing metric methods (e.g. `EmailFailures1h()`). Call it once per successful request in Program.cs's endpoint-11 MapPost block AFTER the send (202 path only — a 400 validation isn't an abuse signal worth counting; keep it simple: emit in the try-block success path). No TelemetryEvents.cs change → no AC-056/EC-056 test impact. Flag as a D-4 metric addition citing TA-10.2 + TA-9.4 (no PII — the counter is just 1).

**Citations:**
- `WaTelemetryReporter.cs` metric-method pattern (EmailFailures1h, StorageBytesActive) — the exact shape to copy.
- TA-4.2#11 "metrics" column; US-008-03 Technical notes ("metrics for request volume (abuse signal)").

### D-5: Endpoint wiring — mirror magic-link MapPost exactly

`POST /api/v1/auth/forgot-password` in Program.cs's auth route block, right after the two T-008-04 routes:
```csharp
app.MapPost("/api/v1/auth/forgot-password", async (ForgotPasswordCommand cmd, ISender sender, WaTelemetryReporter telemetry, CancellationToken ct) =>
{
    try { await sender.Send(cmd, ct); telemetry.ForgotPasswordRequested(); return Results.Accepted(); } // 202 — identical for registered/unregistered (anti-enumeration; TA-4.2#11)
    catch (ForgotPasswordValidationException ex) { throw new BusinessException(ProblemCodes.Validation, ex.Message, ex); }
});
```
Body: `{ email }` → deserialized by the minimal-API binder into the record. 202 Accepted with **no body** for both registered and unregistered — that IS the byte-identical response (verified: magic-link route returns `Results.Accepted()` bare; we return the same).

### D-6: Error codes — none new

Unregistered email is NOT an error path (returns 202); validation reuses `ProblemCodes.Validation` (400). ProblemCodes stays at exactly 18 members. No LinkExpired involvement (that's endpoint 10, already done).

### D-7: Migration — none

AuthToken table + Purpose column pre-exist from T-008-04 (Purpose is byte/TINYINT; value 2 was already in the registry row added by T-008-04). No EF migration.

### D-8: Tests — mirror RequestMagicLinkTests hand-stub style exactly

No Moq anywhere in repo; anonymous-object stubs implementing IAppUserStore/IAuthTokenStore/IEventPublisher/WaTelemetryReporter/ISessionTokenMinter by hand (same as `tests/wa.application.unit/Auth/RequestMagicLinkTests.cs`). New test class covers both AC branches of the exit check + validation.

---

## FILES FOR CODER

| # | Path | Action | What to change |
|---|------|--------|---------------|
| 1 | `src/wa.application/UseCases/Auth/IEmailEventHandler.cs` | **create** | Shared interface per D-3 (fixes the CS8506 in EmailFunction's switch): `public interface IEmailEventHandler { Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct); }` — needs `using wa.domain;`. Lived in wa.application so all three handlers stay SB-free (they already are). Create first — handlers reference it. |
| 2 | `src/wa.application/UseCases/Auth/ForgotPassword.cs` | **create** | `public sealed record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResult>;` + handler (mirror RequestMagicLink.cs layout incl. private statics NormalizeEmail/IsValidEmail/ComputeSha256Hex/GetCorrelationGuid — copy, don't share). Handler ctor: `(IAppUserStore users, IAuthTokenStore tokens, IEventPublisher eventPublisher)`. Flow per D-1: normalize+validate → `_users.FindByEmailAsync` (null ⇒ return result early — no row, no event, no send; anti-enumeration) → token gen (32-byte base64 + SHA-256 hex) → `AuthToken{ Purpose=2 const, ExpiresAtUtc = now + RequestMagicLinkHandler.TtlSeconds }` via IAuthTokenStore.CreateAsync → publish `auth.magic_link_requested` envelope payload `{ email, token (raw base64), expiresAtUtc, purpose = 2 }` (4th field int from the same const — D-3 discriminator) via IEventPublisher → return result. Add `public sealed record ForgotPasswordResult();` + `ForgotPasswordValidationException.ForInvalidEmail()` with message text exactly "A valid email address is required." (same factory style as RequestMagicLink line 141). |
| 3 | `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs` | **create** | Copy of SendMagicLinkEmailHandler, base list implements IEmailEventHandler. Diffs only (D-3): subject const `"Your ProtoDrop link"`; template dir segment "forgot-password"; email.sent/email.failed payloads `kind = "forgot_password"`. Same ctor signature `(IEmailSender, IEventPublisher, IConfiguration, WaTelemetryReporter, ILogger<SendForgotPasswordEmailHandler>)` and same HandleAsync signature as D-3 — copy the rest verbatim incl. CTA URL construction, minutes substitution, DLQ path. Payload parsing reads email/token/expiresAtUtc by name — the extra purpose field is ignored (verified unknown-field safety). |
| 4 | `src/wa.workers/EmailFunction.cs` | **modify** | Inject SendForgotPasswordEmailHandler in ctor. Rewrite the dispatch per D-3: `var handler: IEmailEventHandler? = envelope.Type switch { EventType.TransferCreated => _notificationHandler, EventType.MagicLinkRequested => IsForgotPasswordPayload(envelope) ? _forgotPasswordHandler : _magicLinkHandler, _ => null };` + private static `IsForgotPasswordPayload(EventEnvelope)` helper (serialize payload → JsonDocument.TryGetProperty("purpose") == 2 — same serialize-then-parse idiom the handlers already use). This also fixes the pre-existing CS8506 build failure. Update class XML doc to mention the new arm. |
| 5 | `src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs` | **modify** | One line: add `IEmailEventHandler` to the sealed class base list (HandleAsync signature already matches — verified). No body change. |
| 6 | `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs` | **modify** | One line: add `IEmailEventHandler` (using wa.application.UseCases.Auth) to the SendNotificationEmailsHandler base list (signature already matches — verified). No body change. |
| 7 | `src/wa.workers/Program.cs` | **modify** | Register SendForgotPasswordEmailHandler singleton alongside SendMagicLinkEmailHandler's registration (lines 116–121 pattern): `services.AddSingleton<SendForgotPasswordEmailHandler>(sp => new SendForgotPasswordEmailHandler(sp.GetRequiredService<IEmailSender>(), sp.GetRequiredService<IEventPublisher>(), sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<WaTelemetryReporter>(), sp.GetRequiredService<ILogger<SendForgotPasswordEmailHandler>>()));` |
| 8 | `src/wa.application/Telemetry/WaTelemetryReporter.cs` | **modify** | Add one metric method (D-4): `public void ForgotPasswordRequested() => _logger.LogInformation("forgot_password_requested {count}", 1);` — same shape as EmailFailures1h(). XML doc citing TA-4.2#11 abuse signal + FR-008-6 + TA-9.4 no PII. |
| 9 | `src/wa.api/Program.cs` | **modify** | In the auth route block (immediately after the magic-link MapPost, before redeem): add endpoint-11 MapPost per D-5 — body `{ email }`, ISender, catch ForgotPasswordValidationException → BusinessException(ProblemCodes.Validation), success → `telemetry.ForgotPasswordRequested()` + `Results.Accepted()` (202, no body). No DI change needed in the API project (IAuthTokenStore/IAppUserStore/WaTelemetryReporter already registered; MediatR handler picked up by assembly scan — verify by building). |
| 10 | `src/wa.workers/Emails/templates/forgot-password/en.html` | **create** | HTML email: copy magic-link en.html structure, swap headline/copy for forgot-password tone ("Forgot? We'll sign you in. You can set a new password from your profile afterwards."), same CTA button `{CtaUrl}`, expiry note `{{MinutesRemaining}}`, dark-mode-safe 600 px layout per T-006-05 style. Variables: `{{Email}}`, `{{CtaUrl}}`, `{{MinutesRemaining}}`. |
| 11 | `src/wa.workers/Emails/templates/forgot-password/en.txt` | **create** | Plain-text equivalent of #10 (same CTA URL, no markup). |
| 12 | `tests/wa.application.unit/Auth/ForgotPasswordTests.cs` | **create** | xunit + hand-stubs, mirror RequestMagicLinkTests.cs — NO Moq. Anonymous stubs for IAppUserStore (FindByEmailAsync returns configured AppUser?/null; CreateAsync no-op), IAuthTokenStore, IEventPublisher (records envelopes). Note: RequestMagicLinkTests has no user store to stub — the IAppUserStore stub is new in this task. Tests per D-8: (1) registered email → AuthToken row created with Purpose=2, TokenHash = SHA-256 hex of the payload token (64 chars lowercase), ExpiresAtUtc == now+600 s ± tolerance; event published once, Type==MagicLinkRequested, payload contains email + raw base64 token + expiresAtUtc + purpose=2. (2) **Unregistered email → NO row created (stub CreateAsync not called), NO event published — handler still returns ForgotPasswordResult (exit check item 2).** (3) invalid email (e.g. "not-an-email") → ForgotPasswordValidationException thrown before any store/event call. (4) registered email with different case than stored → normalized to lowercase before existence check + row Email is lowercase. |

---

## ORDERED STEPS

1. **CodeGraph auto-compaction** → then explore/confirm: RequestMagicLink.cs full; SendMagicLinkEmailHandler.cs full; EmailFunction.cs dispatch block + workers Program.cs handler registration lines; IAppUserStore signatures; Program.cs auth route block (T-008-04 MapPost x2) + where WaTelemetryReporter is available in minimal-API scope; RequestMagicLinkTests.cs stub style.
2. **Create IEmailEventHandler.cs** (#1).
3. **Create ForgotPassword.cs** (#2) per D-1/D-6.
4. **Create SendForgotPasswordEmailHandler.cs** (#3) per D-3 (base list implements IEmailEventHandler).
5. **One-line base-list additions** on #5 (SendMagicLinkEmailHandler) and #6 (SendNotificationEmails).
6. **Modify EmailFunction.cs** (#4): ctor param + typed switch + IsForgotPasswordPayload helper — this also fixes the pre-existing CS8506; `dotnet build src/wa.workers -v quiet --nologo` must be green before proceeding.
7. **Create templates** #10 + #11 (copy from magic-link dir, rewrite copy).
8. **Modify workers Program.cs** (#7): register the new handler singleton.
9. **Modify WaTelemetryReporter.cs** (#8): add ForgotPasswordRequested() metric method.
10. **Modify api Program.cs** (#9): add endpoint 11 MapPost (D-5 exact block).
11. **Create tests** #12 per D-8.
12. **Run test gate** (AGENT.md §4): `dotnet test tests/wa.domain.unit` + `dotnet test tests/wa.application.unit`; then `dotnet build src/wa.api -v quiet --nologo` and `dotnet build src/wa.workers -v quiet --nologo` to catch the DI/deserialization fallout (envelope payload extra field + new handler). Fix until green.
13. **Commit**: `feat: T-008-05 forgot-password endpoint anti-enumeration link` — stage ONLY the 12 file paths above individually; no `-A`.

---

## ERROR CASES

| Condition | Path taken | Response / signal | Cited AC/EC |
|---|---|---|---|
| Registered email | row insert (Purpose=2) + event + worker send | `202 Accepted`, no body | FR-008-6, exit check 1 |
| Unregistered email (valid format) | FindByEmailAsync null → early return; no row, no event | **identical** `202 Accepted`, no body | US-008-03 Alternative flows; EC (anti-enumeration) |
| Invalid/missing email | ForgotPasswordValidationException before any store/event call | `400` Problem+JSON VALIDATION (closed code, reused) | TA-4.1.3 |
| Worker send fails (transient) | SB retry via HandleAsync throw (mirrored from SendMagicLinkEmailHandler) | no API-side signal; email.failed on final attempt + DLQ telemetry | TA-5.1, D-6 pattern |
| Token expires / second click | endpoint 10 as-is → LinkExpiredException | `410 LINK_EXPIRED` (T-008-04 machinery; no change here) | AC-008-2/EC-008-3 inherited |

---

## ACCEPTANCE CRITERIA (exit check — verbatim from task file)

- [ ] Registered email → `AuthToken` row with `Purpose=2` created; email sent via worker (dev log-sender visible).
- [ ] Unregistered email → no token row, no send — response body identical to the registered case (no "email not found").
- [ ] Clicking the emailed link signs the user in (endpoint 10 handles `Purpose=2`); second click → expired state (single-use inherited from T-008-04).

---

## ASSUMPTIONS / NOTES for Coder

- **Do NOT add a 13th EventType member** — D-2 resolves the fork to reuse; EnvelopeShapeTests must stay untouched.
- **Do NOT touch RedeemMagicLink.cs / IAuthTokenStore / AuthTokenStore / ProblemCodes / TelemetryEvents / EventEnvelope.cs** — verified no purpose filter in redeem path (FindByHashAsync on hash only) and no new error code needed. If you find otherwise, stop and surface DECISION.
- The payload discriminator is D-3's `purpose` int field (4th). If the Coder finds a cleaner discriminator already present in the codebase, use it but keep both handlers' read-by-name payload parsing (they ignore unknown fields either way).
- "forgot ≠ password reset" copy lives ONLY in the email template + link screen (P1 profile polish is out of scope; no backend change for password set).
