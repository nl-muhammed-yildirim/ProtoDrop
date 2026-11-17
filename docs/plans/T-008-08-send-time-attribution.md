# T-008-08 — Send-time attribution: owner + "From" default (FR-008-7)

**Task:** `T-008-08` | **Spec row:** FR-008-7, AC-008-3 (server half), TA-4.2#3
**Story:** US-008-05-attribution
**Milestone:** T-021 part 8 (send-time owner + "From" default; the My Files list that reads `IX_Transfer_Owner` lands later in T-022 / F-TRF-009)
**Branch:** `feature/F-TRF-008`
**Size:** S

---

## DESIGN OVERVIEW

A `.NET` task on **endpoint 3 (`POST /api/v1/transfers/{id}/send`, T-002-04)** — the single business surface behind send. Today the endpoint is owner-blind: its lambda `(Guid id, HttpRequest request, ISender sender, CancellationToken ct)` carries no session identity, and the handler hard-defaults `transfer.SenderName = request.SenderName ?? "Anonymous"` while leaving `Transfer.OwnerAppUserId` at whatever the **finalize** step stamped it (guest finalize → NULL). T-008-08 makes attribution a **send-time** concern: the owner is the session user id read from the cookie, and "From" defaults to that account's display name (with the T-008-06 email-local-part fallback for an empty name) unless the sender typed a per-transfer override.

Everything it needs already exists — this is wiring + one resolution branch, no schema, no new wire field, no frontend:

1. **The session identity source** (`src/wa.api/Middleware/SessionMiddleware.cs`, T-008-02) is the official single-parse site: a valid cookie lands `Guid` in `HttpContext.Items[SessionMiddleware.SessionUserIdKey]` (constant `"wa.session.userId"`); missing/invalid/expired → item absent. The finalize endpoint **already** reads it with exactly this idiom (`httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?`) — `/send` just has to do the same.
2. **The owner column + index** exist: `Transfer.OwnerAppUserId` (nullable `Guid`) and `IX_Transfer_Owner` (TA-3.3) are in the EF snapshot — My Files' scoped list consumes them later. **No migration.**
3. **The display-name fallback rule** is already frozen by T-008-06: an empty/blank name falls back to the email local part (`Email.Split('@')[0]`, verified verbatim in `AppUserStore.UpdateProfileAsync` and `Signup.DeriveDisplayName`). The handler reuses that exact expression.
4. **The persistence path** persists both mutations for free: `SendStore.SaveSendAsync` runs `_db.Set<Transfer>().Update(transfer)` (full-entity update) inside a transaction — mutating `OwnerAppUserId` + `SenderName` before the save writes both, no column list change.

**Three load-bearing changes:**
- **Command:** `SendTransferCommand` gains a trailing defaulted `Guid? OwnerAppUserId = null` so the endpoint can carry the session identity into the handler without breaking any of its six existing positional call sites.
- **Handler:** new `IAppUserStore _users` ctor dependency + a private `ResolveSenderName(owner, ct)` that maps owner → display name (local-part fallback) or `"Anonymous"`; the activate block overwrites `transfer.OwnerAppUserId = request.OwnerAppUserId` (send-time resolution — a guest-created draft sent while signed in is attributed).
- **Endpoint:** `/send` gains an `HttpContext httpContext` param, reads the session Guid? via the shared idiom, and threads it into the command.

**No `.NET` registration change**: `IAppUserStore` is already registered (`Program.cs` L107, `AddSingleton<IAppUserStore>`), and MediatR auto-wires the handler's ctor from DI — adding a parameter that resolves from DI needs no new line in Program.cs. **No `wa.web` diff** unless a reviewer proves the recipient page doesn't render `From` (it reads `GetPublicTransferResult.From`, which is already a direct snapshot of `Transfer.SenderName` — verified). **No `wa.api.integration` diff**: the command's new param is defaulted and no integration test POSTs `/send` with a session cookie, so guest wire behavior is byte-identical.

---

### D-1: The session reader — switch (i.e. adopt) the official `SessionMiddleware.Items` idiom; no inline TA-9.1 decode

Grounded fact #1 asks for an explicit call. **Decision:** `/send` reads the owner from `HttpContext.Items[SessionMiddleware.SessionUserIdKey]`, mirroring finalize verbatim — NOT a new inline cookie/HMAC reader. Rationale: (a) `SessionMiddleware` is T-008-02's designated single-parse site; (b) it already runs before every endpoint (`app.UseMiddleware<SessionMiddleware>()` at Program.cs L125), so the item is guaranteed present-or-absent by the time `/send` executes; (c) finalize proves the idiom on this exact endpoint family. The "provisional TA-9.1 inline cookie reader" named in the fix round only ever lived on finalize's finalize-path and was already replaced by the Items read — `/send` has **no** reader today at all, so this is a pure addition, not a swap.

The read expression (identical to finalize's, verified verbatim in Program.cs L176):
```csharp
var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;   // missing/invalid/expired → guest (null)
```
`Program.cs` already imports `wa.api.Middleware` (L20) and finalize already references `SessionMiddleware.SessionUserIdKey`, so **no new using** is required.

---

### D-2: Command — add a trailing defaulted owner param

Today, verbatim (`src/wa.application/UseCases/Transfers/SendTransfer.cs` L14–19):
```csharp
public sealed record SendTransferCommand(
    Guid TransferId,
    string? SenderName,
    string? SenderEmail,
    string? Note,
    IReadOnlyList<string> Emails,
    string? Password = null) : IRequest<SendTransferResult?>;
```
Add `Guid? OwnerAppUserId = null` **after** `Password`. Both defaulted params stay at the tail, so all 19 existing positional constructions (1 in Program.cs + 18 in the test file) remain source-compatible with zero edits to their argument lists — only `/send` and the new tests pass a seventh arg:
```csharp
public sealed record SendTransferCommand(
    Guid TransferId,
    string? SenderName,
    string? SenderEmail,
    string? Note,
    IReadOnlyList<string> Emails,
    string? Password = null,
    Guid? OwnerAppUserId = null) : IRequest<SendTransferResult?>;   // T-008-08 (FR-008-7): session owner at send time — guest → null
```

---

### D-3: Handler — new `IAppUserStore` dependency + owner overwrite + "From" resolution

**Ctor/field.** Add the store as a field and ctor parameter. The record's doc comment already names this file T-002-04; append the attribution note. (Add `using wa.application.UseCases.Auth;` — not currently imported.)
```csharp
private readonly ISendStore _store;
private readonly ITransferStore _transferStore;
private readonly ILimitsProvider _limits;
private readonly IEventPublisher _eventPublisher;
private readonly WaTelemetryReporter _telemetry;
private readonly ILogger<SendTransferHandler> _logger;
private readonly IAppUserStore _users;   // T-008-08 (FR-008-7): resolve the display-name "From" default

public SendTransferHandler(
    ISendStore store,
    ITransferStore transferStore,
    ILimitsProvider limits,
    IEventPublisher eventPublisher,
    WaTelemetryReporter telemetry,
    ILogger<SendTransferHandler> logger,
    IAppUserStore users)   // T-008-08 (FR-008-7): session-owner display-name resolution — registered singleton at Program.cs L107
{
    _store = store;
    _transferStore = transferStore;
    _limits = limits;
    _eventPublisher = eventPublisher;
    _telemetry = telemetry;
    _logger = logger;
    _users = users ?? throw new ArgumentNullException(nameof(users));
}
```

**Activate block (today L106–114, verbatim):**
```csharp
        // 5. Activate the transfer: Status=1, expiry from RETENTION_DAYS, sender defaults.
        var now = DateTime.UtcNow;
        transfer.Status = 1;
        transfer.ExpiresAtUtc = now.AddDays(limits.RetentionDays);
        transfer.SenderName = request.SenderName ?? "Anonymous";
        transfer.SenderEmail = request.SenderEmail;
        transfer.Note = request.Note;
```
**Replace with** (the owner line must run *before* the meter at L164 so `transfer.OwnerAppUserId` reflects send-time attribution):
```csharp
        // 5. Activate the transfer: Status=1, expiry from RETENTION_DAYS, sender defaults.
        var now = DateTime.UtcNow;
        transfer.Status = 1;
        transfer.ExpiresAtUtc = now.AddDays(limits.RetentionDays);

        // T-008-08 (FR-008-7): send-time attribution — the session user id IS the owner (guest → null).
        // Unconditional overwrite is deliberate: a guest-created draft sent while signed in becomes attributed,
        // and a guest send stays NULL no matter how the draft was created. Lands ahead of T-022's My Files list.
        transfer.OwnerAppUserId = request.OwnerAppUserId;

        // T-008-08 (US-008-04 happy path step 2): "From" defaults to the display name. The per-transfer override
        // (request.SenderName) still wins (exit check d); else the signed-in user's display name with the T-008-06
        // email-local-part fallback for an empty/blank name; guests stay "Anonymous". SenderName is a SNAPSHOT at
        // send time — later profile edits never rewrite it (FR-002-4).
        transfer.SenderName = request.SenderName ?? await ResolveSenderName(request.OwnerAppUserId, cancellationToken);
        transfer.SenderEmail = request.SenderEmail;
        transfer.Note = request.Note;
```

**New private method** (add alongside the other private helpers at the bottom of `SendTransferHandler`):
```csharp
    /// <summary>
    /// T-008-08 (FR-008-7 / US-008-04 step 2): default "From" for a signed-in send. Resolves the session owner's
    /// display name; an empty/blank name falls back to the email local part (T-008-06 rule — never "From: (blank)").
    /// Unknown or soft-deleted owner (e.g. account deleted mid-draft, T-008-09) → "Anonymous". Guest (null) → "Anonymous".
    /// </summary>
    private async Task<string> ResolveSenderName(Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is not null && await _users.GetByIdAsync(ownerAppUserId.Value, ct) is { } user)
        {
            var name = user.DisplayName.Trim();
            return string.IsNullOrEmpty(name) ? user.Email.Split('@')[0] : name;   // local-part fallback — mirrors AppUserStore.UpdateProfileAsync / Signup.DeriveDisplayName verbatim
        }
        return "Anonymous";
    }
```
`GetByIdAsync(Guid, ct)` returns `AppUser?`, null when unknown **or** soft-deleted (`DeletedAtUtc != null`) — verified in the interface doc and `AppUserStore.GetByIdAsync`. So a deleted account degrades to `"Anonymous"` gracefully rather than throwing. The fallback expression is identical to the frozen T-008-06 rule, so there is no second source of truth to drift.

**No schema change:** the handler mutates two existing columns; `SaveSendAsync`'s full-entity `Update(transfer)` persists both (verified verbatim in `src/wa.infrastructure/Persistence/SendStore.cs`). The meter at L164 already reads `transfer.OwnerAppUserId`, which is now the send-time value — so a signed-in send meters against the correct account.

---

### D-4: DI registration — no new line (verified present)

`IAppUserStore` is registered as a singleton over the adapter-owned context (`Program.cs` L107, verified verbatim):
```csharp
builder.Services.AddSingleton<IAppUserStore>(sp => new wa.infrastructure.Persistence.AppUserStore(sp.GetRequiredService<WaDbContext>()));
```
The handler is picked up by MediatR's assembly scan and its ctor resolved from DI — adding the `IAppUserStore` parameter (now resolvable) needs **no** change to Program.cs registration. The only Program.cs edit is the `/send` endpoint body itself (D-5).

---

### D-5: Endpoint — `/send` reads the session owner and threads it into the command

Today, verbatim (`Program.cs` L202):
```csharp
app.MapPost("/api/v1/transfers/{id}/send", async (Guid id, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    ms.Position = 0;
    using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
    var root = bodyDoc.RootElement;
    var emails = root.TryGetProperty("emails", out var e) ? e.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList() : (IReadOnlyList<string>)[];
    var senderName = root.TryGetProperty("senderName", out var sn) ? sn.GetString() : null;
    var senderEmail = root.TryGetProperty("senderEmail", out var se) ? se.GetString() : null;
    var note = root.TryGetProperty("note", out var n) ? n.GetString() : null;
    var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;

    try
    {
        var result = await sender.Send(new SendTransferCommand(id, senderName, senderEmail, note, emails, password), ct);
```
**Change:** add `HttpContext httpContext` as a param (mirroring finalize's `(Guid draftId, HttpContext httpContext, ...)`) and read the owner via D-1, then pass it as the command's 7th arg:
```csharp
app.MapPost("/api/v1/transfers/{id}/send", async (Guid id, HttpContext httpContext, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    // T-008-08 (FR-008-7): owner AppUserId from the session middleware's Items — single parsing site (T-008-02). Missing/invalid/expired → guest (null).
    var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    ms.Position = 0;
    using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
    var root = bodyDoc.RootElement;
    var emails = root.TryGetProperty("emails", out var e) ? e.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList() : (IReadOnlyList<string>)[];
    var senderName = root.TryGetProperty("senderName", out var sn) ? sn.GetString() : null;
    var senderEmail = root.TryGetProperty("senderEmail", out var se) ? se.GetString() : null;
    var note = root.TryGetProperty("note", out var n) ? n.GetString() : null;
    var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;

    try
    {
        var result = await sender.Send(new SendTransferCommand(id, senderName, senderEmail, note, emails, password, ownerAppUserId), ct);
```
Everything else in the endpoint (404 map on `null` result, size-recheck + validation catch blocks) is untouched. The wire response shape (`{ id, linkId, status, expiresAt, totalBytes, fileCount, recipients }`) does not change — owner and "From" are internal to the entity; the recipient page reads them via endpoint 4's existing `GetPublicTransferResult.From = transfer.SenderName` snapshot (verified verbatim).

---

### D-6: Tests — hand-stub store + one field wire + six new facts (+1 existing fix)

Convention: xunit + **hand-stubs, NO Moq** (mirror `MeProfileTests.StubAppUserStore`). All handler construction goes through the single private `NewHandler(StubLimitsProvider limits)` helper, so adding the store is a **one-point ripple**.

**(a) Add a field and wire it into the existing constructor + helper.** Today:
```csharp
    private readonly StubSendStore _store;
    ...
    private SendTransferHandler NewHandler(StubLimitsProvider limits) =>
        new SendTransferHandler(_store, _meterStore, limits, _eventPublisher, _telemetry, Microsoft.Extensions.Logging.Abstractions.NullLogger<SendTransferHandler>.Instance);
```
Add `private readonly StubAppUserStore _users;`, initialize it in the constructor alongside `_store`/`_meterStore` (`_users = new StubAppUserStore();`), and update `NewHandler` to pass it as the 7th ctor arg (last, before the logger — matching D-3's parameter order):
```csharp
    private SendTransferHandler NewHandler(StubLimitsProvider limits) =>
        new SendTransferHandler(_store, _meterStore, limits, _eventPublisher, _telemetry, Microsoft.Extensions.Logging.Abstractions.NullLogger<SendTransferHandler>.Instance, _users);
```

**(b) One existing test must now pass the owner through the command.** `StorageBytesActive_EmitsRequeriedPostSaveValue_ForAccountOwner` (T-007-05, ~L242) sets `_store.Transfer.OwnerAppUserId = owner;` directly and then sends with a default (owner-less) command. Because D-3 overwrites `transfer.OwnerAppUserId = request.OwnerAppUserId`, that test's command must carry the owner or the entity value clobbers to null and the meter won't fire. Change its one `Handle` call from
```csharp
    var result = await handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None);
```
to pass the owner as the 7th argument:
```csharp
    var result = await handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, owner), CancellationToken.None);
```
The entity-setup line `_store.Transfer.OwnerAppUserId = owner;` becomes redundant but harmless — keep or drop it. (No other test asserts on a non-null `OwnerAppUserId`, so no other ripple.)

**(c) New stub** (add to the file's stub block, mirroring `MeProfileTests.StubAppUserStore` — only `GetByIdAsync` is exercised by these tests):
```csharp
    private sealed class StubAppUserStore : IAppUserStore
    {
        public Dictionary<Guid, AppUser?> UserById { get; } = new();

        // T-008-08 (D-3): the only member the send handler touches — null on miss OR soft-delete.
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserById.TryGetValue(userId, out var user) ? user : null);

        // Members below are unused by these tests — no-op implementations keep the stub lean (IAppUserStore shape, T-008-01).
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult<AppUser?>(null);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }
```
(Add `using wa.application.UseCases.Auth;` to the test file for `IAppUserStore`.)

**(d) Six new `[Fact]`s**, each building a signed-in/guest user in `_users.UserById[owner] = ...` as needed. Helper shape for a user row:
```csharp
    private AppUser NewUser(Guid id, string displayName, string email) => new()
    { Id = id, Email = email, DisplayName = displayName, Theme = "system", PlanId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };
```

1. **Exit (a)** — `Handle_SignedIn_OwnerSetFromSession_AndFromDefaultsToDisplayName`:
   - `_store.Transfer` draft (Status 0), `_users.UserById[owner] = NewUser(owner, "Ada Lovelace", "ada@example.com")`.
   - Handle with `new SendTransferCommand(transferId, null, null, null, [], null, owner)`.
   - Assert `_store.Transfer.OwnerAppUserId == owner` and `_store.Transfer.SenderName == "Ada Lovelace"`.
2. **Exit (b)** — `Handle_Guest_OwnerNull_Anonymous`:
   - Draft draft; command with 7th arg `null` (no user set up).
   - Assert `_store.Transfer.OwnerAppUserId == null` and `SenderName == "Anonymous"`.
3. **Exit (c)** — `Handle_DraftGuest_SentSignedIn_AttributesToSessionUser`:
   - Draft created as guest (`_store.Transfer.OwnerAppUserId = null`), then send while signed in: command carries `owner`, user exists with display name.
   - Assert `_store.Transfer.OwnerAppUserId == owner` (overwritten from null) and `SenderName == <display name>` — proves send-time, not draft-time, resolution.
4. **Exit (d)** — `Handle_OverrideWins_SenderNameBeatsDisplayName`:
   - User with display name set; command passes an explicit `request.SenderName = "My Custom Name"` AND owner.
   - Assert `SenderName == "My Custom Name"` (override wins) and `OwnerAppUserId == owner` still set.
5. **Fallback** — `Handle_EmptyDisplayName_FallsBackToLocalPart`:
   - User with `DisplayName = ""` (or `"  "`), Email `"ada@example.com"`, owner set, no override.
   - Assert `SenderName == "ada"` (local-part fallback, never blank).
6. **Edge** — `Handle_OwnerDeletedMidDraft_SignedInFromFallsBackToAnonymous`:
   - Owner set on command; `_users.UserById[owner] = null` (row soft-deleted/unknown → GetByIdAsync returns null).
   - Assert `SenderName == "Anonymous"` and `OwnerAppUserId == owner` (attribution preserved even though the display name is gone — consistent with endpoint 13's not-found semantics).

---

### ERROR CASES

No **new** closed-set `ProblemCode` is introduced — attribution resolves inside the handler to a string/nullable Guid before any exception path. All reachable errors are pre-existing and already mapped by the `/send` endpoint (unchanged):

| Situation | Response | Code (closed set — verified member) |
|---|---|---|
| Transfer id not found at send time | 404 JSON problem body (`result is null`) | `NOT_FOUND` (existing, T-002-04) |
| Send-time size re-check exceeds `MaxTransferSize` (T-007-04/AC-007-1) | 422 JSON problem body | `TRANSFER_SIZE_EXCEEDED` (existing) |
| Recipient count > `MaxEmails` after normalize/dedupe | 400 JSON problem body | `VALIDATION` (existing) |
| Signed-in owner's account deleted mid-draft (`GetByIdAsync` → null, soft-deleted) | 201 — `SenderName` defaults to `"Anonymous"`, `OwnerAppUserId` still set; **no error** | n/a — graceful fallback in `ResolveSenderName` (D-3/D-6 test 6) |
| Signed-in owner with empty/blank display name | 201 — `SenderName` = email local part; **no error** | n/a — T-008-06 local-part rule (D-3/D-6 test 5) |
| Guest send (missing/expired cookie → item absent → null owner) | 201 — `OwnerAppUserId = NULL`, `SenderName` from override or `"Anonymous"`; **no error** | n/a — anonymous by design (FR-008-7 growth loop) |

The existing closed wire shapes are consumed verbatim and unchanged:
- `POST /api/v1/transfers/{id}/send` → `{ id, linkId, status: "Active", expiresAt, totalBytes, fileCount, recipients }` (201). **No field added** to the body.
- `GET /api/v1/public/transfers/{linkId}` → `GetPublicTransferResult.From = transfer.SenderName` (snapshot) — the recipient page's "From" line; this task only changes what value gets snapshotted at send time.

---

### FILES FOR CODER (exact commit boundary)

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Transfers/SendTransfer.cs` | MODIFY | D-2: add trailing `Guid? OwnerAppUserId = null` to the record. D-3: add `using wa.application.UseCases.Auth;`, add `IAppUserStore _users` field + ctor param (last, before logger), rewrite the activate block (L106–114) with the unconditional owner overwrite + `SenderName = request.SenderName ?? await ResolveSenderName(...)`, and add the private async `ResolveSenderName(Guid?, CancellationToken)` method. |
| `src/wa.api/Program.cs` | MODIFY | D-5: on the `/send` endpoint (L202), add `HttpContext httpContext` to the lambda param list, read `var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;` before the body parse, and pass it as the 7th arg of `new SendTransferCommand(...)`. No DI registration line (D-4), no new using. |
| `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs` | MODIFY | D-6: add `using wa.application.UseCases.Auth;`, add `_users` field + init, wire it into `NewHandler`'s 7th ctor arg; update the one existing T-007-05 meter test to pass owner as the command's 7th arg; add the private `NewUser(...)` helper + `StubAppUserStore`; add the six new `[Fact]` methods (a)–(f). |

**Verified, NO diff:**
- **Schema / migration:** `Transfer.OwnerAppUserId` + `IX_Transfer_Owner` already in the EF snapshot (TA-3.3); `SaveSendAsync`'s full-entity `Update(transfer)` persists both mutated columns — no new column list, no migration file.
- **`src/wa.web/`:** LinkScreen sends `senderName: blankOr(fromName)` (blank → JSON null → server default), and the recipient page reads endpoint 4's `From = transfer.SenderName`. No frontend change unless a reviewer proves otherwise (cite the exact line).
- **`src/wa.api.integration/`:** no test POSTs `/send` with a session cookie; the defaulted command param keeps guest wire behavior identical.
- **`ProblemCodes.cs`, `Program.cs` registration, DI:** untouched (D-4).

**Excluded from the commit:** no migration; no new ProblemCode; no npm/wa.web files; only the 3 files above, staged individually (never `git add -A` — the repo carries unrelated untracked `NUL` + `docs/plans/T-008-03-auth-screen-ui.md` and a modified `.codegraph/.gitignore`).

---

### GATES (Definition of Done)

```bash
cd C:/Users/myild/source/repos/ProtoDrop
dotnet build src/wa.api -v quiet                       # green; 4 pre-existing CS8506-ish warnings tolerated, 0 errors
dotnet test tests/wa.domain.unit -v quiet              # expect 45/45 passing (unchanged — domain untouched)
dotnet test tests/wa.application.unit -v quiet         # expect 223 + 6 new = 229 passing, 0 failing
```

Do **NOT** run the Testcontainers integration suite in this gate (`wa.api.integration` is out of scope per the task).

**Exit check (manual / by test, after gates pass — all four binding):**
- [ ] **(a)** Signed-in send → `Transfer.OwnerAppUserId == session user id`; recipient page shows "From: {display name}" (AC-008-3 server half). *Verified by:* D-6 test 1.
- [ ] **(b)** Guest send → `OwnerAppUserId == NULL`; same transfer flow, no attribution. *Verified by:* D-6 test 2.
- [ ] **(c)** Draft created as a guest, sent while signed in → attributed to the account (send-time resolution, not draft time). *Verified by:* D-6 test 3.
- [ ] **(d)** Per-transfer "From" override still wins over the display-name default. *Verified by:* D-6 test 4.

Commit: exactly one `feat: T-008-08 send-time attribution owner From default` commit staging **only** the 3 files in FILES FOR CODER, individually (not `-A`).

---

### ADR (Architecture Decision Records) — none required
No new NuGet packages, no migration, no new closed-set `ProblemCode`, no frontend change. Two documented choices a reviewer may revisit:
- **Unconditional owner overwrite** (`transfer.OwnerAppUserId = request.OwnerAppUserId`) rather than "only when present." It makes "guest send → always NULL" airtight (a draft finalized signed-in but sent as guest de-attributes to anonymous) and reads as true "send-time resolution," at the cost of one existing T-007-05 test now passing the owner via its command (D-6b). A reviewer preferring the "only-when-present" variant should flag it — that variant would leave a signed-in-finalized, guest-sent draft attributed.
- **Handler-side display-name resolution** (a new `IAppUserStore` ctor dep) rather than resolving the name in Program.cs and passing a pre-computed string. It keeps the endpoint thin (TA-4.2a) and puts the fallback rule next to the owner logic; the cost is one more DI dependency on an otherwise DB-only handler.
