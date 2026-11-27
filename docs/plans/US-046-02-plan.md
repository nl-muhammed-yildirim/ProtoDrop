# US-046-02 plan — Trust that recipients are unaffected (F-XCT-003, milestone T-067 slice 2)

## 1. Goal

Prove — with a closed-list unit test and one real-handler integration fact — that the owner email setting (US-046-01) mutes only owner notifications: recipient (transfer created), auth (magic link / forgot password) and billing emails never consult it (FR-046-4, AC-046-3, both story ACs), and document the EC-046-1 one-in-flight-message window. **Exit check:** new tests green (unit run locally; integration fact compiles locally, runs in CI / at feature end since Docker is down), full §4 gate green, TA-6.7 + REPO-FACTS carry the boundary and the window.

**In scope:** US-046-02 ACs (recipient still mailed while owner is off; no email to the owner's own address for the transfer's lifecycle; billing email sent while off), AC-046-3, FR-046-4, story edge case "EmailSuppression rows are independent", T-067 "one in-flight message window documented". **Out:** FR-046-8 digest (P2), Playwright E2E, any new owner-notification email (none is specced in this slice), status flips in backlog/PROGRESS/story file (orchestrator close-out).

## 2. Design & Decisions

**What is already true in code (verified, no production change needed).**
- f-email dispatches exactly four handlers, chosen by event type (`src/wa.workers/EmailFunction.cs:66`): transfer created → `SendNotificationEmailsHandler` (recipients), magic link requested → `SendMagicLinkEmailHandler` / `SendForgotPasswordEmailHandler` (auth), billing email requested → `SendBillingEmailHandler` (billing). Every other EventType member (`src/wa.domain/EventType.cs:11`) — including download completed, transfer expired — sends no email.
- `OwnerNotificationGate.MaySendAsync` (`src/wa.application/UseCases/EmailSettings/OwnerNotificationGate.cs:24`) has **no production caller**: it is only registered (`src/wa.workers/WorkerServices.cs:155`) and exercised by tests. None of the four handlers takes the gate or `IEmailNotificationsStore` in its constructor → recipient, auth and billing emails are exempt by construction (FR-046-4, AC-046-3).
- The recipient handler never mails the sender's own address (`src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs:130`, FR-006-3), and no owner-addressed lifecycle email exists yet → story AC 1 "no email is sent to my own address for that transfer's lifecycle events" holds today.
- Billing handler: "Billing mail is transactional — no suppression check" (`src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs:16`); both billing kinds (billing_payment_failed, billing_grace_ended) ride the same event type, so any future billing kind is exempt by the same rule.
- The settings helper copy already states the one-sentence boundary from the story ("Recipients always get their download emails; billing emails always arrive." — `src/wa.web/src/core/i18n/locales/en.json:250`). No web change.
- EC-046-1 window is implemented (gate reads the column per call, no cache — slice-1 fact `Gate_RealStore_ReadsAtSendTime`) but documented only in the feature spec, not in TA-6.7 / REPO-FACTS.

**Therefore this slice is tests + docs only.** No src/ change.

**D1 — Closed-list classification lives in a test, not in production code (codebase-design: no shallow module without a caller).** A production `EmailAudience` map would have no caller today (no owner-notification handler exists) or would require refactoring the f-email dispatch switch for no behavioral gain. Instead one workers unit test class pins the classification at the three seams a new email must pass through, so adding an email kind fails the build-gate tests until its author classifies it explicitly:
1. **Handlers** — every concrete `IEmailEventHandler` (`src/wa.application/UseCases/Auth/Interfaces/IEmailEventHandler.cs:9`) found by reflection in the wa.application and wa.workers assemblies must appear in a closed table (handler type → audience). Rule: the constructor takes `OwnerNotificationGate` **iff** audience is owner; no handler takes `IEmailNotificationsStore` directly (owner handlers must go through the gate so guest/flag rules FR-046-5/6 stay in one place).
2. **Dispatch** — the `EmailFunction` constructor's handler parameters (`src/wa.workers/EmailFunction.cs:27`, all but the logger) must equal the table's keys: wiring a new handler into f-email without classifying it fails.
3. **Templates** — the folder names under the output `Emails/templates` (copied into the workers.unit bin by the wa.workers content item) must equal a closed table (folder → audience): transfer = recipient; magic-link, forgot-password = auth; billing-payment-failed, billing-grace-ended = billing.
4. **Event types** — every EventType member maps to recipient / auth / billing / none in a closed table (13 members); a new member forces the author to decide whether it mails and to whom. Tripwire only (the switch in `EmailFunction.Run` is private), stated as such in the test comment.
Tradeoff: reflection tests are structural, not behavioral — the behavioral proof is D2.

**D2 — One real-handler integration fact in the existing E2E harness (TA-6.7, US-006-01 precedent).** `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` already drives the real API send → real `SendNotificationEmailsHandler` + real templates + `Wa:Email:Sender=log`, capturing rendered mails. Add one fact (the story's "one owner-off account, one transfer, three email types — exactly one suppression path"): seed owner AppUser with `EmailNotificationsEnabled = false`, flag feature.emailSettings = true, an `EmailSuppression` for (muted@example.com, owner address). Send a guest transfer to r@example.com + muted@example.com + the owner's own address, then attach the owner (`Transfer.OwnerAppUserId`, `src/wa.domain/Transfer.cs:13`) **after** the send (the worker reads the row at send time; avoids any owner check on the guest send endpoint). Assertions:
- real `OwnerNotificationGate` over the real store + real `LimitsProvider` says **false** for that owner (the fixture really is "off" — the only suppression path);
- recipient worker run → exactly one mail, to r@example.com (recipient unaffected; muted address still muted by its own per-sender row; owner's own address never mailed);
- billing worker run (`SendBillingEmailHandler`, kind billing_payment_failed step 1, to the owner) → exactly one mail to the owner with subject "Your ProtoDrop payment didn't go through" (`SendBillingEmailHandler.cs:22`) — AC-046-3;
- afterwards: owner column still false, the suppression row still present (independent rows, story EC).
Per-fact fresh containers (class-level IAsyncLifetime) keep the flag mutation isolated. Docker is down locally → the Coder compiles it; it runs in CI / the feature-end integration pass (pitfalls.md).

**D3 — No unit fact on SendNotificationEmailsHandler.** The handler has no setting dependency, so a unit "setting off" fact cannot express anything D1 (structure) and D2 (behavior) do not already prove; FR-006-3 is already covered in `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs`.

**D4 — Docs (exit check "one in-flight message window documented").** TA-6.7 gains a step 7; REPO-FACTS §3 gains one bullet next to the existing `/auth/me` line (`docs/REPO-FACTS.md:72`). Both part of the Coder's commit (they are the slice's deliverable, not status bookkeeping).

Golden rules: no package, no limit literal, no new event/telemetry/Problem code, no schema change; the PII gate is untouched (no src/ change; test addresses are example.com fixtures as in the existing facts).

## 3. Implementation Plan

1. **Closed-list unit test.** CREATE `tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs` (snippet below). Verify: dotnet test tests/wa.workers.unit with filter FullyQualifiedName~EmailAudienceClassificationTests, then the whole project. Sanity check while iterating (do not commit): temporarily delete one row from the Handlers table and one from TemplateFolders, confirm facts 1/3/4 fail, then restore.
2. **Integration fact.** MODIFY `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs`: add Fact C before the `// ---- Helpers ----` line (L212); extract the worker IConfiguration in `RunWorkerAsync` (L247–253) into `private static IConfiguration WorkerConfig()` and reuse it; add helper `RunBillingWorkerAsync(Guid userId, string email)`. Add usings `wa.application.UseCases.Billing` and `wa.application.UseCases.EmailSettings`. Verify locally: `dotnet build tests/wa.api.integration` (Docker down). With Docker: dotnet test tests/wa.api.integration with filter FullyQualifiedName~EmailLogSenderE2ETests.
3. **Docs.** `docs/03-technical-architecture.md` TA-6.7: append after step 6 (L672):
   "7. Owner email setting (F-XCT-003, FR-046-3/4): only owner-notification emails consult `AppUser.EmailNotificationsEnabled`, through `OwnerNotificationGate.MaySendAsync` at send time. Recipient (transfer created), auth (magic link / forgot password) and billing (billing email requested, every kind) handlers never take the gate — pinned by `tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs`. No owner-notification email exists yet; a new one must call the gate before sending. The column is read per message (no cache), so a toggle misses at most the one message already being processed — the one-in-flight-message window (EC-046-1)."
   `docs/REPO-FACTS.md` after L72 add a bullet: "f-email audience (F-XCT-003 US-046-02): handlers = SendNotificationEmailsHandler (recipient), SendMagicLinkEmailHandler + SendForgotPasswordEmailHandler (auth), SendBillingEmailHandler (billing); none takes OwnerNotificationGate (it has no production caller yet). Closed lists pinned in tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs (handlers, EmailFunction ctor, template folders, EventType). Window: one in-flight message (EC-046-1)."
   Check line endings with `git diff` (docs may be CRLF — pitfalls.md).
4. **Gate (AGENT.md §4, Docker down).** `dotnet test tests/wa.domain.unit`, `dotnet test tests/wa.application.unit`, `dotnet test tests/wa.workers.unit`, `dotnet build tests/wa.api.integration`, dotnet format src/wa.slnx (verify-no-changes mode), from repo root `node scripts/check-theme-tokens.mjs`, `node scripts/check-contrast.mjs`, `node scripts/check-telemetry-pii.mjs && node scripts/check-telemetry-pii.mjs selftest`. Web not touched → web gate not required. One commit `feat: US-046-02 recipients unaffected — closed-list email audience tests + E2E proof` with exactly the files in the table + the two docs files.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs` | CREATE | Closed lists: handlers ↔ gate rule, EmailFunction ctor, template folders, EventType |
| `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` | MODIFY | Fact C owner-off: recipient mailed, owner/muted not, billing mailed; WorkerConfig + RunBillingWorkerAsync helpers |

Also in the commit boundary (docs, not parsed by verify_plan): docs/03-technical-architecture.md (TA-6.7 step 7) and docs/REPO-FACTS.md (§3 bullet), text in step 3.

**New file — `tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs`:**
```csharp
using System.Reflection;
using Microsoft.Extensions.Logging;
using Xunit;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Billing;
using wa.application.UseCases.EmailSettings;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.workers.unit.Email;

/// <summary>
/// US-046-02 (FR-046-4, AC-046-3): the owner email setting mutes OWNER notifications only. Closed lists — a new email
/// handler, template folder or event type fails here until its author classifies it explicitly.
/// </summary>
public class EmailAudienceClassificationTests
{
    private const string Recipient = "recipient"; // governed by per-sender EmailSuppression (F-TRF-006), never the sender's setting
    private const string Auth = "auth";
    private const string Billing = "billing";     // money emails must arrive
    private const string Owner = "owner";         // must call OwnerNotificationGate.MaySendAsync (FR-046-3); none exist yet
    private const string None = "none";

    private static readonly Dictionary<Type, string> Handlers = new()
    {
        [typeof(SendNotificationEmailsHandler)] = Recipient,
        [typeof(SendMagicLinkEmailHandler)] = Auth,
        [typeof(SendForgotPasswordEmailHandler)] = Auth,
        [typeof(SendBillingEmailHandler)] = Billing,
    };

    private static readonly Dictionary<string, string> TemplateFolders = new()
    {
        ["transfer"] = Recipient,
        ["magic-link"] = Auth,
        ["forgot-password"] = Auth,
        ["billing-payment-failed"] = Billing,
        ["billing-grace-ended"] = Billing,
    };

    // Tripwire: EmailFunction.Run's switch is private — this table forces a decision per new EventType member.
    private static readonly Dictionary<EventType, string> EventTypes = new()
    {
        [EventType.TransferCreated] = Recipient,
        [EventType.TransferExpired] = None,
        [EventType.TransferDeleted] = None,
        [EventType.EmailSent] = None,
        [EventType.EmailFailed] = None,
        [EventType.DownloadCompleted] = None,
        [EventType.ZipGenerated] = None,
        [EventType.UserCreated] = None,
        [EventType.AccountDeleted] = None,
        [EventType.PlanChanged] = None,
        [EventType.AdminAction] = None,
        [EventType.MagicLinkRequested] = Auth,
        [EventType.BillingEmailRequested] = Billing,
    };

    [Fact]
    public void EveryEmailHandler_IsClassified()
    {
        var found = new[] { typeof(IEmailEventHandler).Assembly, typeof(EmailFunction).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEmailEventHandler).IsAssignableFrom(t))
            .OrderBy(t => t.FullName)
            .ToList();
        Assert.Equal(Handlers.Keys.OrderBy(t => t.FullName), found);
    }

    [Fact]
    public void OnlyOwnerHandlers_TakeTheOwnerGate_NoneReadsTheColumnDirectly() // FR-046-4: recipient/auth/billing exempt by construction
    {
        foreach (var (handler, audience) in Handlers)
        {
            var deps = handler.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
            Assert.Equal(audience == Owner, deps.Contains(typeof(OwnerNotificationGate)));
            Assert.DoesNotContain(typeof(IEmailNotificationsStore), deps);
        }
    }

    [Fact]
    public void EmailFunction_DispatchesExactlyTheClassifiedHandlers()
    {
        var wired = typeof(EmailFunction).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => !typeof(ILogger).IsAssignableFrom(t))
            .OrderBy(t => t.FullName);
        Assert.Equal(Handlers.Keys.OrderBy(t => t.FullName), wired);
    }

    [Fact]
    public void EveryTemplateFolder_IsClassified()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Emails", "templates");
        var folders = Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(TemplateFolders.Keys.OrderBy(n => n, StringComparer.Ordinal), folders);
    }

    [Fact]
    public void EveryEventType_IsClassified()
    {
        Assert.Equal(Enum.GetValues<EventType>().OrderBy(e => e), EventTypes.Keys.OrderBy(e => e));
        Assert.Empty(EventTypes.Where(kv => kv.Value == Owner)); // US-046-02: no owner-notification email kind exists yet
    }
}
```
Notes: `Path.GetFileName` returns `string?` under nullable — if the analyzer complains on the Assert.Equal overload, project with `.Select(d => Path.GetFileName(d)!)`. `EmailFunction` is in namespace wa.workers (`src/wa.workers/EmailFunction.cs:11`) — the test namespace wa.workers.unit.Email resolves it via the parent namespace; add `using wa.workers;` if the compiler does not. The workers.unit csproj references only wa.workers (transitive wa.application/wa.domain) — no csproj change. The template folders reach the test bin through wa.workers' `Content Include="Emails\templates\**\*"` (`src/wa.workers/wa.workers.csproj:14`; StubbleTemplateRendererTests already rely on it).

**`tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` — existing code the Coder changes:**
```csharp
245:    private async Task RunWorkerAsync(EventEnvelope envelope)
246:    {
247:        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
248:        {
249:            ["Wa:Url:Public"] = PublicOrigin,
250:            ["Wa:Jwt:Secret"] = TestSecret,
251:            ["Wa:Email:FromAddress"] = FromAddress,
252:            ["Wa:Email:Sender"] = "log"
253:        }).Build();
254:
255:        var workerDb = new AppDbContextFactory(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options); // fresh context per store call
256:        var telemetry = new WaTelemetryReporter(_loggerFactory.CreateLogger<WaTelemetryReporter>(), Encoding.UTF8.GetBytes(TestSecret));
```
Change: L247–253 → `var config = WorkerConfig();` with the dictionary moved into `private static IConfiguration WorkerConfig()`.

New helper (after RunWorkerAsync):
```csharp
    /// <summary>US-046-02: the billing arm of f-email, composed like WorkerServices (templates from the linked Emails/templates).</summary>
    private async Task RunBillingWorkerAsync(Guid userId, string email)
    {
        var config = WorkerConfig();
        var telemetry = new WaTelemetryReporter(_loggerFactory.CreateLogger<WaTelemetryReporter>(), Encoding.UTF8.GetBytes(TestSecret));
        var handler = new SendBillingEmailHandler(
            EmailSenderSelector.Create(config, _loggerFactory),
            new InMemoryEventPublisher(),
            config,
            telemetry,
            _loggerFactory.CreateLogger<SendBillingEmailHandler>(),
            Path.Combine(AppContext.BaseDirectory, "Emails", "templates"),
            TimeProvider.System);
        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.BillingEmailRequested,
            Version = 1,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            PartitionKey = userId.ToString("n"),
            Payload = new { userId, email, subscriptionId = Guid.NewGuid(), kind = BillingEmailSchedule.Kind, step = 1 }
        };
        await handler.HandleAsync(envelope, 1, 5, CancellationToken.None);
    }
```
(Envelope shape copied from `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs:50`; `BillingEmailSchedule.Kind` = billing_payment_failed, `src/wa.application/UseCases/Billing/BillingEmailSchedule.cs:14`.)

New Fact C (before L212), outline — the Coder writes it in the file's style:
```csharp
    // ---- Fact C: US-046-02 / AC-046-3 / FR-046-4 ----

    [Fact]
    public async Task OwnerSettingOff_RecipientsAndBillingStillSent_OnlyOwnerPathSuppressed()
    {
        const string owner = "alice@example.com";
        Guid ownerId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            var planId = await db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
            db.Set<AppUser>().Add(new AppUser { Id = ownerId, Email = owner, DisplayName = "Alice Example", PlanId = planId, Theme = "system", CreatedAtUtc = DateTime.UtcNow, EmailNotificationsEnabled = false });
            db.Set<EmailSuppression>().Add(new EmailSuppression { Id = Guid.NewGuid(), Address = "muted@example.com", SenderEmail = owner, CreatedAtUtc = DateTime.UtcNow });
            (await db.Set<FeatureFlag>().FirstAsync(f => f.Key == FeatureGates.EmailSettings)).Value = "true";
            await db.SaveChangesAsync();
        }

        var transferId = await SetupDraftTransferAsync(("owner.txt", 512));
        await SendAsync(transferId, new[] { "r@example.com", "muted@example.com", owner }, owner);
        await using (var db = NewDb()) // attach the owner after the guest send: the worker reads the row at send time
            await db.Set<Transfer>().Where(t => t.Id == transferId).ExecuteUpdateAsync(s => s.SetProperty(t => t.OwnerAppUserId, (Guid?)ownerId));

        // The fixture's owner really is "off": the real gate over the real store + flag registry suppresses owner mail.
        var workerDb = new AppDbContextFactory(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var flags = new LimitsProvider(
            new PlansCache(new PlansStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
            new FlagsCache(new FlagsStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow));
        var gate = new OwnerNotificationGate(new EmailNotificationsStore(workerDb), flags, _loggerFactory.CreateLogger<OwnerNotificationGate>());
        Assert.False(await gate.MaySendAsync(ownerId, CancellationToken.None));

        var before = _capture.Mails().Count;
        await RunWorkerAsync(TransferCreatedEnvelope(transferId));
        var recipientMails = _capture.Mails().Skip(before).ToList();
        Assert.Equal("r@example.com", Assert.Single(recipientMails).To); // recipient unaffected; muted stays muted; owner never mailed

        before = _capture.Mails().Count;
        await RunBillingWorkerAsync(ownerId, owner);
        var billing = Assert.Single(_capture.Mails().Skip(before).ToList()); // AC-046-3: billing exempt
        Assert.Equal(owner, billing.To);
        Assert.Equal("Your ProtoDrop payment didn't go through", billing.Subject);

        await using (var db = NewDb()) // independent rows: neither mechanism touched the other
        {
            Assert.False(await db.Set<AppUser>().AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.EmailNotificationsEnabled).SingleAsync());
            Assert.Equal("muted@example.com", (await db.Set<EmailSuppression>().AsNoTracking().SingleAsync()).Address);
        }
    }
```
Context: `LimitsProvider`, `PlansCache`, `FlagsCache`, `FeatureGates` are in wa.application.Limits (already imported, L20); `PlansStore`/`FlagsStore`/`AppDbContextFactory`/`EmailNotificationsStore` in wa.infrastructure.Persistence (imported, L27); `Plan`, `AppUser`, `FeatureFlag`, `Transfer`, `EmailSuppression` in wa.domain (L24). Seeding mirrors `tests/wa.api.integration/EmailNotificationsSettingTests.cs:50`. The FlagsCache in the API host may hold the old flag value — irrelevant: only the worker side reads the flag here. If `ExecuteUpdateAsync`'s `(Guid?)` cast is flagged as redundant by IDE0004, drop it.

## 5. Open Questions / Risks

- **AC-046-3 names a "Pro renewal invoice" email that does not exist** — F-BIL-002 today has billing_payment_failed and billing_grace_ended only. The fact uses billing_payment_failed; any future billing kind rides billing email requested and is exempt by D1's rule (the handler never takes the gate). Not blocking; noted for the reviewer.
- **No owner-notification email exists**, so "the owner's own address gets nothing for the transfer's lifecycle" is vacuously true today, plus FR-006-3. The closed-list test (Owner arm + gate rule) is what forces a future owner email (expiry warning, download-limit approaching) to call the gate. Those emails are separate, unspecced work.
- **Integration fact not runnable locally** (Docker down): only compiled in this slice; first real run is CI / feature-end pass. Risk points: guest send then owner attach (chosen to avoid owner checks on the send route); suppression address matching is exact lowercase (fixture uses lowercase).
- **Reflection test over two assemblies**: if a test-only `IEmailEventHandler` stub is ever added to wa.workers (not tests), fact 1 flags it — intended.
- No DECISION needed: no package, schema, limit, PII/security, env var or pending-decision change.
