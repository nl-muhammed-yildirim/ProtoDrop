# T-008-06 — GET/PATCH/DELETE /auth/me + logout (endpoints 12–13)

**Task:** `T-008-06` | **Spec row:** FR-008-4, FR-008-8, TA-4.2#12–13
**Story:** US-008-04-profile (+ US-008-06 delete half)
**Milestone:** T-021 part 6 (profile read/update + logout; account deletion re-homes transfers)
**Branch:** `feature/F-TRF-008`
**Size:** M

---

## DESIGN OVERVIEW

Three new use cases behind endpoint 13 (`GET`, `PATCH`, `DELETE /api/v1/auth/me`) plus one endpoint-only route for logout (endpoint 12). The session middleware from T-008-02 already resolves the cookie into `HttpContext.Items[SessionMiddleware.SessionUserIdKey]` as a `Guid?` — these endpoints are the first consumers of that value. No migration: `AppUser.Theme`, `DisplayName`, and `DeletedAtUtc` all exist in `WaDbContextModelSnapshot.cs` (verified). No new domain event type, telemetry name, or ProblemCode: `EventType.AccountDeleted`, `TelemetryEvents.AccountDeleted`, `ProblemCodes.NotFound`/`Unauthenticated` are all already members of their closed sets (verified — this task is a pure consumer of the T-054 outbox + T-056 problem pipeline).

The load-bearing new surface is three methods on the **existing** `IAppUserStore` port (D-1); that breaks exactly four existing test hand-stubs, all updated in this commit. The DELETE soft-delete + transfer re-home run inside ONE explicit transaction (`SoftDeleteWithReHomeAsync`, D-1 — same pattern as `ExpiryDeletionStore.DeleteTransfersAsync`), and the `account_deleted` publish happens right after that commit (D-2 step 3) — same two-steps-in-one-handler shape as RequestMagicLink.

### D-1: IAppUserStore gains 3 methods; AppUserStore implements them; 4 existing test stubs get the new members added

The port already has exactly two methods (`FindByEmailAsync`, `CreateAsync` — verified verbatim in both `IAppUserStore.cs` and the hand-stubs). This task adds:

```csharp
// src/wa.application/UseCases/Auth/IAppUserStore.cs — ADD (keep existing two untouched):

/// <summary>T-008-06: return the row for the session user id, or null when unknown OR soft-deleted (DeletedAtUtc != null) — endpoint 13 maps that to 404 NOT_FOUND.</summary>
Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct);

/// <summary>T-008-06 (FR-008-4): partial profile update. null argument = leave the column alone; displayName is trimmed, and an empty result falls back to the email local part (US-008-04 alternative flow — never "From: (blank)").</summary>
Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct);

/// <summary>T-008-06 / FR-008-8: soft-delete the AppUser row and re-home its transfers in ONE transaction — returns false when the user id is unknown.</summary>
Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct);
```

EF implementation (`src/wa.infrastructure/Persistence/AppUserStore.cs`) mirrors `ExpiryDeletionStore.DeleteTransfersAsync` (verified pattern: explicit transaction + bulk update):

```csharp
public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
    _db.Set<AppUser>().FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAtUtc == null, ct);

public async Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct)
{
    var user = await _db.Set<AppUser>().FirstAsync(u => u.Id == userId, ct); // caller pre-checked via GetByIdAsync (no-row → 401/404 before this runs)
    if (displayName is not null)
    {
        var trimmed = displayName.Trim();
        user.DisplayName = string.IsNullOrEmpty(trimmed)
            ? user.Email.Split('@')[0]   // US-008-04 alternative flow: fallback to the email local part
            : trimmed;
    }
    if (theme is not null)
        user.Theme = theme;               // caller validated against system/light/dark before this runs
    await _db.SaveChangesAsync(ct);
}

public async Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct)
{
    // Mirror ExpiryDeletionStore.DeleteTransfersAsync's idiom EXACTLY (verified): open the tx with `await using`,
    // perform the writes, CommitAsync at the end — no try/catch/rollback wrapper.
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // 1. Soft-delete the user row (30-day operator undo window — hard delete is P1 admin; US-008-06 edge case).
    var updatedUsers = await _db.Set<AppUser>()
        .Where(u => u.Id == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAtUtc, deletedAtUtc), ct);

    // 2. Re-home owned transfers: Status IN (0,1,3) — drafts/active/download-limit go anonymous; expired (2) rows
    //    die via the f-delete-transfers job anyway (Status values verified verbatim in Transfer.cs). SenderName is
    //    a snapshot column — never touched here (FR-002-4).
    await _db.Set<Transfer>()
        .Where(t => t.OwnerAppUserId == userId && t.Status != 2)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.OwnerAppUserId, (Guid?)null), ct);

    await tx.CommitAsync(ct);
    return updatedUsers > 0; // false only in the race where a concurrent DELETE already committed — handler maps to the same not-found shape
}
```

The AppUser row is soft-deleted, NOT removed — `Transfer.OwnerAppUserId` stays satisfiable for the Status=2 expired rows we intentionally leave pointed at a deleted user, so no cascade concerns. The event publish (D-2 step 3) happens AFTER this method commits, inside the handler — same two-steps-in-one-handler shape as RequestMagicLink (insert → publish), NOT an outer transaction wrapping store + publisher.

**Blast radius — exactly 4 existing hand-stub test files implement IAppUserStore and will fail to compile when the port gains members:**
- `tests/wa.application.unit/Auth/LoginHandlerTests.cs`
- `tests/wa.application.unit/Auth/SignupHandlerTests.cs`
- `tests/wa.application.unit/Auth/RedeemMagicLinkTests.cs`
- `tests/wa.application.unit/Auth/ForgotPasswordTests.cs`

Each stub gets the three new members added as minimal no-op implementations (the Login/Signup/Redeem/Forgot handlers don't call them — e.g. `GetByIdAsync => Task.FromResult<AppUser?>(null)`, `UpdateProfileAsync => Task.CompletedTask`, `SoftDeleteWithReHomeAsync => Task.FromResult(true)`). These are compile fixes only — do not restructure those files or their assertions.

**Citations:**
- `IAppUserStore.cs` + `AppUserStore.cs` (current 2-method surface, verbatim above) — the port to extend.
- `ExpiryDeletionStore.DeleteTransfersAsync` (src/wa.infrastructure/Persistence/) — transaction idiom to mirror.
- `Transfer.cs` Status values: 0=Draft, 1=Active, 2=Expired, 3=DownloadLimit (verified). Re-home SQL from US-008-06 Technical notes + T-008-06 task spec §4 verbatim.
- FR-008-4, FR-008-8, EC-008-2; US-008-04 happy path + Alternative flows (empty display name fallback, Unicode NVARCHAR(100)); US-008-06 happy path (re-home SQL) + edge cases (soft-delete undo window).

### D-2: One handler file `MeProfile.cs` — GetProfile / UpdateProfile / DeleteAccount commands, house-style exceptions

New file `src/wa.application/UseCases/Auth/MeProfile.cs`, one namespace, three handlers + one exception class (same single-file style as Login.cs / RequestMagicLink.cs):

```csharp
public sealed record GetProfileCommand(Guid UserId) : IRequest<GetProfileResult>;
public sealed record UpdateProfileCommand(Guid UserId, string? DisplayName, string? Theme) : IRequest<GetProfileResult>;
public sealed record DeleteAccountCommand(Guid UserId) : IRequest<DeleteAccountResult>();

// Wire record bound from the JSON body by Minimal API — the session supplies UserId (NOT in the body), so PATCH is
// constructed at Program.cs's single construction site: new UpdateProfileCommand(userId.Value, req.DisplayName, req.Theme).
public sealed record UpdateProfileRequest(string? DisplayName, string? Theme);

// Wire shape pinned at the endpoint's single construction site (TA-4.2a): identical to LoginResult minus SessionToken —
// the cookie already carries the session, /auth/me does not re-mint one.
public sealed record GetProfileResult(
    Guid UserId, string Email, string DisplayName, string Theme, string PlanCode);

public sealed record DeleteAccountResult();

public sealed class MeProfileException : Exception   // house style: private ctor + static factories (LoginWrongPasswordException pattern, Login.cs lines 129–134 verbatim)
{
    private MeProfileException(string message) : base(message) { }
    public static MeProfileException ForNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound)); // title from the closed table — spec pins "no row → 404"
    public static MeProfileException ForInvalidTheme() =>
        new("theme must be one of: system, light, dark.");       // 400 VALIDATION at the endpoint
    public static MeProfileException ForDisplayNameTooLong(string maxLength) =>
        new($"displayName must be at most {maxLength} characters after trimming."); // 400 VALIDATION — literal 100 lives only in the constant below
    public static MeProfileException ForEmptyPatch() =>
        new("Provide at least one of displayName or theme.");   // 400 VALIDATION — an all-null PATCH is a silent no-op
}

public const int DisplayNameMaxLength = 100; // NVARCHAR(100), TA-3.2 — verified in the snapshot; the single re-check site (UpdateProfileHandler)
```

**GetProfileHandler:** ctor `(IAppUserStore users, IPlansStore plans)` — no telemetry, no publisher. `users.GetByIdAsync(userId)` → null (unknown OR soft-deleted) → `MeProfileException.ForNotFound()`; plan via `plans.GetByIdAsync(user.PlanId)` mirroring LoginHandler exactly (null plan row → same not-found shape, no new code). Returns the DTO with the entity's `DisplayName`/`Theme` verbatim.

**UpdateProfileHandler:** ctor `(IAppUserStore users)`.
1. Existence gate FIRST: `GetByIdAsync` → null → ForNotFound() (401/404 at endpoint — same as GET; a PATCH against a dead cookie is not "invalid theme").
2. **Partial semantics live in the record**: `DisplayName == null && Theme == null` → 400 VALIDATION ("Provide at least one of displayName or theme.") — an all-null PATCH changes nothing and would be a silent success, which leaks no information but wastes a round trip; mirror the "server re-check" house style (Login's password-length check).
3. **theme validation** (only when non-null): must be exactly `"system"`, `"light"`, or `"dark"` → else 400 VALIDATION. Citation: US-008-04 happy path item 1 + FR-008-4 ("theme (system/light/dark)"). **These three values are NOT in the Open-Decisions-and-Constants.md FROZEN registry — cite spec + entity only, do not invent a registry row.**
4. **displayName validation** (only when non-null): after trim, length ≤ 100 → else 400 VALIDATION (the column is `NVARCHAR(100)`, TA-3.2 — verified in snapshot; server re-check mirrors FR-008-5's password-length house pattern). Empty-after-trim falls back to email local part inside the store (D-1), so no blank "From:" ever persists.
5. `users.UpdateProfileAsync(userId, displayName, theme)` → return a fresh DTO from a second `GetByIdAsync` + plan resolution — PATCH response body is byte-shaped like GET's (client can re-render without a follow-up request).

**DeleteAccountHandler:** ctor `(IAppUserStore users, IEventPublisher eventPublisher, WaTelemetryReporter telemetry)` — all three (step 4 uses the reporter).
1. Existence gate: `users.GetByIdAsync(userId)` → null → ForNotFound(). Capture the entity for the envelope payload + partition key BEFORE the soft-delete wipes it from reads.
2. `ok = await users.SoftDeleteWithReHomeAsync(user.Id, DateTime.UtcNow, ct)` — inside THIS transaction the handler does not yet publish (see D-3: publish happens after commit).

   **Correction — event publish placement:** `IEventPublisher.PublishAsync` persists its outbox row "same transaction as business data, where one exists" (verified in `Ports/Events.cs`). AppUserStore's methods each own their saves; the cleanest atomic shape that matches RequestMagicLink (token insert + publish both inside the handler, no explicit outer transaction there either) is: **publish AFTER SoftDeleteWithReHomeAsync returns true**, with a comment that EC-054-1's outbox re-publish covers broker failure — the user row is already committed soft-deleted by then. The "account_deleted emitted once" AC (EC-008-2) is about exactly ONE event for one deletion, which holds: the handler runs once per successful DELETE and the outbox dedups on EventId. Keep this simple — do NOT invent an outer transaction wrapping store + publisher; mirror RequestMagicLink's two-steps-in-one-handler shape (insert → publish).
3. Build the envelope EXACTLY as `RequestMagicLink.Handle` does (verified verbatim, lines 70–84):

```csharp
var payload = new { userId = user.Id };   // PII sweep reference (US-008-06 edge case / TA-5.3) — spec pins "carrying the userId"
var envelope = new EventEnvelope
{
    EventId = Guid.NewGuid(),
    Type = EventType.AccountDeleted,      // 9th of 12 members — already in the frozen list, untouched file
    Version = 1,
    OccurredAtUtc = deletedAtUtc,         // the same instant passed into SoftDeleteWithReHomeAsync (the fact happened then)
    CorrelationId = GetCorrelationGuid(), // copy RequestMagicLink's private static verbatim (same convention as SendTransferHandler)
    PartitionKey = user.Email,            // email — same partitioning as the other auth.* events (RequestMagicLink uses email too)
    Payload = payload
};
await _eventPublisher.PublishAsync(envelope, cancellationToken);
```

4. Telemetry: exactly ONE `_telemetry.LogEvent(TelemetryEvents.AccountDeleted)` after publish — `account_deleted` is already a member of the TA-10.2 closed set (verified in `TelemetryEvents.cs`). No new event name, no new metric method on WaTelemetryReporter.
5. Return `new DeleteAccountResult()`.

**Citations:**
- `RequestMagicLink.cs` full — envelope construction + correlation-guid derivation + exception house style (verbatim pattern above).
- `Login.cs` full — DTO shape, plan resolution via IPlansStore, exception factories.
- `Ports/Events.cs` — IEventPublisher contract (outbox semantics, IsPublished retry).
- FR-008-4/8, EC-008-2; US-008-04 alternative flows; US-008-06 Technical notes (re-home SQL verbatim); T-008-06 task spec §Instructions 1–4 + Exit check.

### D-3: Program.cs — four routes at the end of the auth block, after /login

Mirror the existing auth route block (verified verbatim above; append AFTER the `/api/v1/auth/login` MapPost):

```csharp
// T-008-06 (FR-008-4 / TA-4.2#13): GET — GetProfileCommand from session userId; wire shape = login's minus sessionToken.
app.MapGet("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) => { ... });

// T-008-06 (FR-008-4 / TA-4.2#13): PATCH — UpdateProfileRequest bound from the JSON body; UserId from the session cookie
// (constructed at this single site: new UpdateProfileCommand(userId.Value, req.DisplayName, req.Theme)).
app.MapPatch("/api/v1/auth/me", async (UpdateProfileRequest req, HttpContext ctx, ISender sender, CancellationToken ct) => { ... });

// T-008-06 (TA-4.2#12): POST /auth/logout — clear the session cookie; no use case (cookie clearing is endpoint-level,
// the middleware owns parsing — same as T-008-02). No telemetry beyond metrics (task spec §3).
app.MapPost("/api/v1/auth/logout", (HttpContext ctx) => { ... });

// T-008-06 (FR-008-8 / TA-4.2#13): DELETE — soft-delete + re-home + account_deleted; clears the cookie on success.
app.MapDelete("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) => { ... });
```

**Shared session-read idiom** (write it once as a local function inside Program.cs, or inline per-route — coder's choice, keep it identical to what T-008-02 documented):

```csharp
Guid? userId = ctx.Items[SessionMiddleware.SessionUserIdKey] as Guid?;   // null for guests (const verified: SessionMiddleware.SessionUserIdKey)
if (userId is null) return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);
```

Per route:
- **GET:** `new GetProfileCommand(userId.Value)` → on success: `Results.Json(new { userId = result.UserId.ToString("n"), email = ..., displayName = ..., theme = ..., planCode = ... }, statusCode: 200)` — the 'n'-form userId + exact key set of signup/login (TA-4.2a single-construction-site rule, Program.cs lines ~392–401 verbatim). No sessionToken field.
- **PATCH:** bind `UpdateProfileRequest` from body; construct the command with `userId.Value`; catch: `MeProfileException.ForNotFound` family → 404 NOT_FOUND (see below); validation factories (invalid theme / too-long name / empty PATCH) → `BusinessException(ProblemCodes.Validation, ex.Message, ex)` — the inner-exception-preservation pattern from T-008-01 fix round.
- **logout:** `ctx.Response.Cookies.Delete("wa.session"); return Results.Ok();` — 200 no body (the middleware only refreshes when a token is present; deleting leaves the next request as guest). No use case, no MediatR.
- **DELETE:** on success → clear cookie exactly like logout + return `Results.Json(new { userId = result.UserId.ToString("n") }, statusCode: 200)` — body mirrors GET's shape minus profile fields so clients get a uniform envelope; comment that account_deleted already went out via the outbox (EC-008-2). Catch MeProfileException as in PATCH.

**401 vs 404 decision (spec-verified, NOT my invention):** the task spec pins "no row for the session user id (deleted) → **404**" (Instructions §1) and the exit check's guest case is "**401/404**". Both codes are closed-set members. Use:
- **401 UNAUTHENTICATED** — no cookie / expired / bad signature (the session-read idiom above). Guest with no row to look up; purpose-built member (ProblemCodes.cs verified).
- **404 NOT_FOUND** — valid cookie but the AppUser row is missing/soft-deleted (GET/PATCH/DELETE all map `MeProfileException.ForNotFound` here). Spec's literal pin.

Do NOT add a 19th ProblemCode member — golden-rule comment in ProblemCodes.cs pins exactly 18.

**Citations:**
- Program.cs auth block lines ~385–520 (all four existing MapPost routes verbatim — cookie set/delete shape, error mapping, wire shape).
- `SessionMiddleware.SessionUserIdKey` const + `Items['wa.session.userId'] as Guid?` (verified).
- TA-4.1.3 closed problem codes; TA-4.2a single-construction-site wire shape.

### D-4: Test file — new `tests/wa.application.unit/Auth/MeProfileTests.cs`, xunit + hand-stubs only (no Moq)

Mirror `LoginHandlerTests.cs` structure exactly (verified pattern): hand-stub stores, real `WaTelemetryReporter` over a recording logger for telemetry asserts where needed. Tests to include:
1. **GET happy path:** stub returns user+plan → DTO fields verbatim; plan code resolved via IPlansStore stub.
2. **GET unknown / soft-deleted id** (store returns null) → MeProfileException.ForNotFound — assert exception type, not message.
3. **PATCH displayName only:** store captures args → `theme == null` passed through unchanged + displayName trimmed; empty name → fallback to email local part asserted on the returned DTO (store stub simulates D-1 behavior: set DisplayName = "ada" for user with email ada@x.com).
4. **PATCH theme only:** `displayName == null`, theme="dark"; invalid theme "purple" → validation exception (assert via exception type/factory).
5. **PATCH both null** → validation exception (empty PATCH rejected).
6. **DELETE happy path:** stub records SoftDeleteWithReHomeAsync called once with the right userId; event publisher records exactly ONE envelope — assert `Type == EventType.AccountDeleted`, payload serializes to contain `"userId"` (PII sweep reference), PartitionKey == user email, CorrelationId non-empty Guid.
7. **DELETE unknown id** → ForNotFound before soft-delete is ever called (assert store's SoftDelete not invoked).

Plus the four stub-file updates from D-1 (compile fixes, no new assertions there). Total: 7 tests in MeProfileTests.cs + 4 files with additive stub members only.

### ERROR CASES

| Situation | Response | Code (closed set — verified member) |
|---|---|---|
| GET/PATCH/DELETE without a session cookie, or the row is soft-deleted/unknown | 401 JSON problem body | `Unauthenticated` (existing; NO new member) |
| PATCH: invalid theme value / displayName >100 chars after trim / all-null body | 400 VALIDATION via BusinessException(ProblemCodes.Validation,...) | `Validation` (existing) |
| DELETE succeeds but broker delivery fails | outbox row stays unpublished → re-publish picks it up (EC-054-1); HTTP already succeeded | n/a — event-level, not wire |

No new ProblemCode member anywhere in this task. ProblemCodes.cs is **not** in the file boundary unless a reviewer proves otherwise — it must stay at exactly 18 members with its golden-rule comment untouched.

### FILES FOR CODER (exact commit boundary)

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Auth/IAppUserStore.cs` | MODIFY | +3 methods (D-1 verbatim signatures). Existing two methods unchanged. |
| `src/wa.infrastructure/Persistence/AppUserStore.cs` | MODIFY | Implement the 3 new methods (D-1; mirror ExpiryDeletionStore transaction idiom for SoftDeleteWithReHomeAsync). |
| `src/wa.application/UseCases/Auth/MeProfile.cs` | CREATE | 3 commands + results + handlers + MeProfileException (D-2 verbatim shape). |
| `src/wa.api/Program.cs` | MODIFY | Append the four Map routes at the end of the auth block (D-3); no changes above the `/auth/login` route. |
| `tests/wa.application.unit/Auth/MeProfileTests.cs` | CREATE | 7 tests per D-4. |
| `tests/wa.application.unit/Auth/LoginHandlerTests.cs` | MODIFY | Add 3 stub members to the hand-stub IAppUserStore (D-1) — compile fix only, no assertion changes. |
| `tests/wa.application.unit/Auth/SignupHandlerTests.cs` | MODIFY | Same as above. |
| `tests/wa.application.unit/Auth/RedeemMagicLinkTests.cs` | MODIFY | Same as above. |
| `tests/wa.application.unit/Auth/ForgotPasswordTests.cs` | MODIFY | Same as above. |

**Excluded from the commit (repo carries them untracked — stage per-path, never `-A`):** the stray `NUL` file + `docs/plans/T-008-03-auth-screen-ui.md`.
**No migration.** **No new package/ADR line.** **wa.web untouched → no npm gates.**

### GATES (Definition of Done)

```bash
dotnet test tests/wa.domain.unit          # expect 45/45 — EventType closed list untouched
dotnet test tests/wa.application.unit     # expect 216 + 7 = 223 passing, 0 failing
dotnet build src/wa.api -v quiet --nologo # green (pre-existing CS8524 warnings tolerated)
```

Do NOT run the slow Testcontainers integration suite — deferred to feature end. Commit: exactly one `feat: T-008-06 ...` commit staging ONLY the 9 files above, individually.
