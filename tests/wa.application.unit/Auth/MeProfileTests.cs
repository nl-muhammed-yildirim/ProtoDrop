using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Auth;

// T-008-06 (FR-008-4 / FR-008-8 / TA-4.2#13): the three /auth/me handlers + their error shapes.
// Hand-stub stores only (T-008-01 style); real WaTelemetryReporter over a recording logger where needed.
public class MeProfileTests
{
    private readonly StubAppUserStore _users;
    private readonly StubPlansStore _plans;
    private readonly TelemetryRecorder _telemetryLogger;

    public MeProfileTests()
    {
        _users = new StubAppUserStore();
        _plans = new StubPlansStore();
        _telemetryLogger = new TelemetryRecorder();
    }

    private AppUser SeedRealAppUser(Guid id) =>
        new()
        {
            Id = id,
            Email = "ada@example.com", // email local part is "ada" — fallback target for empty displayName (US-008-04 alternative flow)
            DisplayName = "Ada Lovelace",
            Theme = "system",
            PlanId = _plans.Plan.Id,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

    // ---- D-4 test 1: GET happy path — stub returns user + plan → DTO fields verbatim ----

    [Fact]
    public async Task GetProfile_HappyPath_ReturnsDtoVerbatim_FromStubUserAndPlan()
    {
        var userId = Guid.NewGuid();
        _users.UserById[userId] = SeedRealAppUser(userId);

        var handler = new GetProfileHandler(_users, _plans);
        var result = await handler.Handle(new GetProfileCommand(userId), CancellationToken.None);

        Assert.Equal(userId, result.UserId);
        Assert.Equal("ada@example.com", result.Email);
        Assert.Equal("Ada Lovelace", result.DisplayName); // verbatim from the entity
        Assert.Equal("system", result.Theme);
        Assert.Equal("free", result.PlanCode); // plan code resolved via IPlansStore stub
    }

    // ---- D-4 test 2: GET unknown / soft-deleted id (store returns null) → MeProfileException.ForNotFound ----

    [Fact]
    public async Task GetProfile_UnknownOrSoftDeletedId_ReturnsForNotFound()
    {
        var userId = Guid.NewGuid(); // not in the stub's UserById map
        _users.UserById[userId] = null; // simulate soft-deleted row: store returns null

        var handler = new GetProfileHandler(_users, _plans);
        await Assert.ThrowsAsync<MeProfileException>(async () =>
            await handler.Handle(new GetProfileCommand(userId), CancellationToken.None));
    }

    // ---- D-4 test 3: PATCH displayName only — theme == null passed through; empty name → fallback to email local part ----

    [Fact]
    public async Task UpdateProfile_DisplayNameOnly_ThemeNull_PassesThrough_TrimsAndFallsBackForEmpty()
    {
        var userId = Guid.NewGuid();
        _users.UserById[userId] = SeedRealAppUser(userId);

        var handler = new UpdateProfileHandler(_users, _plans, new StubOrgNames(), new StubPlanContext(true));

        // Case A: normal displayName — trimmed; theme untouched (null passed through).
        var result = await handler.Handle(new UpdateProfileCommand(userId, "  Grace Hopper ", null), CancellationToken.None);
        Assert.Equal("Grace Hopper", result.DisplayName); // trimmed (D-1: store trims)
        Assert.Equal("system", result.Theme); // theme was null → left alone (D-1 partial-update contract)

        // Case B: empty-after-trim displayName — the store falls back to the email local part (US-008-04 alternative flow).
        _users.UserById[userId]!.DisplayName = "Grace Hopper";
        await handler.Handle(new UpdateProfileCommand(userId, "   ", null), CancellationToken.None);
        Assert.Equal("ada", _users.UserById[userId]!.DisplayName); // email local part — never a blank "From:"
    }

    // ---- D-4 test 4: PATCH theme only — displayName == null; invalid theme → validation exception ----

    [Fact]
    public async Task UpdateProfile_InvalidTheme_ThrowsValidation()
    {
        var userId = Guid.NewGuid();
        _users.UserById[userId] = SeedRealAppUser(userId);

        var handler = new UpdateProfileHandler(_users, _plans, new StubOrgNames(), new StubPlanContext(true));
        await Assert.ThrowsAsync<MeProfileException>(async () =>
            await handler.Handle(new UpdateProfileCommand(userId, null, "purple"), CancellationToken.None));
    }

    // ---- D-4 test 5: PATCH both null → validation exception (empty PATCH rejected) ----

    [Fact]
    public async Task UpdateProfile_BothNull_ThrowsValidation()
    {
        var userId = Guid.NewGuid();
        _users.UserById[userId] = SeedRealAppUser(userId);

        var handler = new UpdateProfileHandler(_users, _plans, new StubOrgNames(), new StubPlanContext(true));
        await Assert.ThrowsAsync<MeProfileException>(async () =>
            await handler.Handle(new UpdateProfileCommand(userId, null, null), CancellationToken.None));
    }

    // ---- D-4 test 6: DELETE happy path — SoftDeleteWithReHomeAsync once; exactly ONE AccountDeleted envelope ----

    [Fact]
    public async Task DeleteAccount_HappyPath_SoftDeletesOnce_EmitsSingleEnvelope()
    {
        var userId = Guid.NewGuid();
        _users.UserById[userId] = SeedRealAppUser(userId);
        var publisher = new StubEventPublisher();
        var handler = new DeleteAccountHandler(
            _users,
            publisher,
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            new RecordingBlobs());

        await handler.Handle(new DeleteAccountCommand(userId), CancellationToken.None);

        Assert.Single(_users.SoftDeletes); // SoftDeleteWithReHomeAsync called exactly once (D-1)
        Assert.Equal(userId, _users.SoftDeletes[0].UserId);

        // Exactly ONE envelope — EC-008-2: one event for one deletion (dedup on EventId).
        Assert.Single(publisher.Envelopes);
        var envelope = publisher.Envelopes[0];
        Assert.Equal(EventType.AccountDeleted, envelope.Type); // 9th of 12 members — already in the frozen list
        Assert.Equal("ada@example.com", envelope.PartitionKey); // email partitioning (RequestMagicLink pattern)
        Assert.NotEqual(Guid.Empty, envelope.CorrelationId); // CorrelationId is a non-empty Guid (GetCorrelationGuid convention)

        // PII sweep reference: payload serializes to contain the userId key.
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(envelope.Payload!);
        Assert.Contains("userId", payloadJson);
    }

    // ---- D-4 test 7: DELETE unknown id → ForNotFound before soft-delete is ever called ----

    [Fact]
    public async Task DeleteAccount_UnknownId_ThrowsBeforeSoftDeleteIsCalled()
    {
        var userId = Guid.NewGuid(); // not in the stub's UserById map (unknown or soft-deleted)

        var handler = new DeleteAccountHandler(
            _users,
            new StubEventPublisher(),
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            new RecordingBlobs());

        await Assert.ThrowsAsync<MeProfileException>(async () =>
            await handler.Handle(new DeleteAccountCommand(userId), CancellationToken.None));

        // The existence gate runs FIRST — SoftDeleteWithReHomeAsync is never invoked for an unknown id.
        Assert.Empty(_users.SoftDeletes);
    }

    // ---- US-021-04 (decision 4): the org logo is deleted with the account ----

    [Fact]
    public async Task DeleteAccount_WithLogo_DeletesLogoBlob()
    {
        var userId = Guid.NewGuid();
        var user = SeedRealAppUser(userId);
        user.OrgLogoBlobPath = $"avatars/{userId:N}/logo";
        _users.UserById[userId] = user;
        var blobs = new RecordingBlobs();
        var handler = new DeleteAccountHandler(
            _users,
            new StubEventPublisher(),
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            blobs);

        await handler.Handle(new DeleteAccountCommand(userId), CancellationToken.None);

        Assert.Equal([$"avatars/{userId:N}/logo"], blobs.Deleted);
    }

    [Fact]
    public async Task DeleteAccount_LogoDeleteFails_StillCompletes()
    {
        var userId = Guid.NewGuid();
        var user = SeedRealAppUser(userId);
        user.OrgLogoBlobPath = $"avatars/{userId:N}/logo";
        _users.UserById[userId] = user;
        var publisher = new StubEventPublisher();
        var handler = new DeleteAccountHandler(
            _users,
            publisher,
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            new RecordingBlobs { Throw = true });

        var result = await handler.Handle(new DeleteAccountCommand(userId), CancellationToken.None);

        Assert.NotNull(result); // soft-delete already committed; a storage failure must not become a 500
        Assert.Single(_users.SoftDeletes);
        Assert.Single(publisher.Envelopes);
    }

    // ---- Stubs (T-008-01 style — hand-written stubs, no Moq) ----

    private sealed class RecordingBlobs : IBlobStore
    {
        public List<string> Deleted { get; } = [];
        public bool Throw { get; set; }
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("storage down");
            Deleted.Add(path);
            return Task.CompletedTask;
        }
        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubAppUserStore : IAppUserStore
    {
        public Dictionary<Guid, AppUser?> UserById { get; } = new();
        public List<(Guid UserId, DateTime DeletedAtUtc)> SoftDeletes { get; } = [];

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult(AppUserByEmail(lowercasedEmail));
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask; // unused in these tests

        private AppUser? AppUserByEmail(string email) => UserById.Values.FirstOrDefault(u => u?.Email == email);

        // D-1 GetByIdAsync: returns the row or null (null = unknown OR soft-deleted — handler maps to 404).
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserById.TryGetValue(userId, out var user) ? user : null);

        // D-1 UpdateProfileAsync: simulates the store's behavior (trim + fallback to email local part for empty name).
        public async Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct)
        {
            if (!UserById.TryGetValue(userId, out var user) || user is null) return; // caller pre-checked via GetByIdAsync
            if (displayName is not null)
            {
                var trimmed = displayName.Trim();
                user.DisplayName = string.IsNullOrEmpty(trimmed) ? user.Email!.Split('@')[0] : trimmed;
            }
            if (theme is not null)
                user.Theme = theme;
            await Task.CompletedTask;
        }

        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct)
        {
            SoftDeletes.Add((userId, deletedAtUtc)); // record the call (D-4 test 6/7 assertions)
            UserById[userId] = null; // simulate the row going soft-deleted (readers now see null → ForNotFound)
            return Task.FromResult(UserById.ContainsKey(userId)); // true when the row existed — handler maps to the not-found shape on false
        }
    }

    private sealed class StubOrgNames : IOrgNameStore
    {
        public Task SetOrgNameAsync(Guid userId, string? orgName, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubPlansStore : IPlansStore
    {
        // FR-004: "Free by default" is a seeded row — test stub mirrors the seed (TA-3.4).
        public Plan Plan { get; } = new() { Id = Guid.NewGuid(), Code = "free", Name = "Free" };

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) =>
            planCode == "free" ? Task.FromResult(Plan) : throw new PlanUnknownException(planCode);

        // T-008-02: by-Id read — returns the seeded row or null (never throws).
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plan.Id == planId ? Plan : null);
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Envelopes { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    /// <summary>T-008-01 style: records exactly what M.E.L hands to the logger (same as WaTelemetryReporterTests).</summary>
    internal sealed class TelemetryRecorder : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText, object?[] Args);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Record(state?.ToString() ?? string.Empty, ExtractArgs(state!));

        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args) =>
            Record(message ?? string.Empty, args);

        private static object?[] ExtractArgs(object state) =>
            ((state as System.Collections.IEnumerable)?
                .Cast<System.Collections.Generic.KeyValuePair<string, object?>>()
                .Where(kv => kv.Key != "{OriginalFormat}")
                .Select(kv => (object?)kv.Value).ToArray()) ?? [];

        private void Record(string renderedText, object?[] args) => Entries.Add(new Entry(renderedText, args));
    }

    private sealed class StubPlanContext(bool branding) : IPlanContextAccessor
    {
        public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanContext(branding ? "pro" : "free", new LimitsRecord(), new PlanFeatures { Branding = branding }));
    }
}
