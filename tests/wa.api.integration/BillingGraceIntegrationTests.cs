using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using wa.application.Telemetry;
using wa.application.UseCases.Billing;
using wa.domain;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-019-05 (AC-019-2 second half, EC-019-3 timer side, replay without extension): the real SQL mirror, ledger and the f-expire
/// grace sub-step, time-travelled with explicit nowUtc. SQL 2022 Testcontainers only — no Azurite, no HTTP host.
/// </summary>
public class BillingGraceIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime P = T0.AddDays(7).AddHours(1);
    private const string Email = "payer@example.com";

    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private StripeBillingStore _stripe = null!;
    private BillingSequenceStore _sequence = null!;
    private InMemoryEventPublisher _events = null!;
    private SendBillingEmailsHandler _emailPass = null!;
    private EndBillingGraceHandler _gracePass = null!;
    private Guid _userId;
    private Guid _proId;
    private Guid _freeId;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();

        var options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(options);
        await _db.Database.MigrateAsync();

        _freeId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _proId = await _db.Set<Plan>().Where(p => p.Code == "pro").Select(p => p.Id).FirstAsync();
        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Grace Test",
            PlanId = _proId,
            Theme = "system",
            CreatedAtUtc = T0
        });
        _db.Set<Subscription>().Add(new Subscription
        {
            Id = Guid.NewGuid(),
            AppUserId = _userId,
            StripeSubId = "sub_it",
            StripeCustomerId = "cus_it",
            PlanCode = "pro",
            Status = "active",
            CreatedAtUtc = T0,
            UpdatedAtUtc = T0
        });
        await _db.SaveChangesAsync();

        var factory = new AppDbContextFactory(options);
        _stripe = new StripeBillingStore(factory);
        _sequence = new BillingSequenceStore(factory);
        _events = new InMemoryEventPublisher();
        _emailPass = new SendBillingEmailsHandler(_sequence, _events, NullLogger<SendBillingEmailsHandler>.Instance);
        _gracePass = new EndBillingGraceHandler(
            new BillingGraceStore(factory),
            _events,
            new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, [1, 2, 3]),
            NullLogger<EndBillingGraceHandler>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private static StripeSubscriptionChange Change(string eventId, string type, string status) =>
        new(eventId, type, "sub_it", "cus_it", status, null, null, null);

    private async Task<Subscription> SubAsync() => await _db.Set<Subscription>().AsNoTracking().SingleAsync();

    private async Task<Guid> UserPlanIdAsync() =>
        await _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == _userId).Select(u => u.PlanId).SingleAsync();

    private List<JsonElement> Payloads() =>
        _events.GetByTopic("core").Select(e => JsonDocument.Parse(e.Json).RootElement.GetProperty("payload").Clone()).ToList();

    private int GraceEndedEmailCount() =>
        Payloads().Count(p => p.TryGetProperty("kind", out var k) && k.GetString() == "billing_grace_ended");

    private int PlanChangedGraceCount() =>
        Payloads().Count(p => p.TryGetProperty("reason", out var r) && r.GetString() == "grace_ended");

    private async Task PauseAsync(string eventId, DateTime at) =>
        await _stripe.ApplySubscriptionAsync(Change(eventId, "customer.subscription.paused", "paused"), null, at, default);

    [Fact]
    public async Task FullAc0192_PaymentFailed_ThenPause_StaysPro_ThenFreeAfter14Days_OnceOnly()
    {
        await _sequence.StartPaymentFailedAsync("evt_1", "in_1", "sub_it", T0, default);
        Assert.Equal(1, await _emailPass.RunAsync(T0.AddDays(3), default));
        Assert.Equal(1, await _emailPass.RunAsync(T0.AddDays(7), default));

        var pause = await _stripe.ApplySubscriptionAsync(
            Change("evt_pause", "customer.subscription.paused", "paused"), null, P, default);
        Assert.Equal(StripeApplyOutcome.Applied, pause.Outcome);

        var sub = await SubAsync();
        Assert.Equal("paused", sub.Status);
        Assert.Equal(P.AddDays(14), sub.GraceEndsAtUtc);
        Assert.Equal(_proId, await UserPlanIdAsync()); // during grace I am still on Pro

        Assert.Equal(0, await _gracePass.RunAsync(P.AddDays(13), default));
        Assert.Equal(_proId, await UserPlanIdAsync());

        Assert.Equal(1, await _gracePass.RunAsync(P.AddDays(14), default));
        Assert.Equal(_freeId, await UserPlanIdAsync());
        Assert.Equal(1, PlanChangedGraceCount());
        Assert.Equal(1, GraceEndedEmailCount());

        Assert.Equal(0, await _gracePass.RunAsync(P.AddDays(14), default));
        Assert.Equal(0, await _gracePass.RunAsync(P.AddDays(14).AddMinutes(15), default));
        Assert.Equal(1, PlanChangedGraceCount());
        Assert.Equal(1, GraceEndedEmailCount());
        Assert.Equal(1, await _db.Set<JobRun>().AsNoTracking().CountAsync(j => j.JobKey.StartsWith("billing-grace:")));
    }

    [Fact]
    public async Task Ec0193_ResumedBeforeTimer_NoDowngrade_NoEmail()
    {
        await PauseAsync("evt_pause", P);

        await _stripe.ApplySubscriptionAsync(
            Change("evt_active", "customer.subscription.resumed", "active"), _proId, P.AddDays(5), default);

        var sub = await SubAsync();
        Assert.Equal("active", sub.Status);
        Assert.Null(sub.GraceEndsAtUtc);

        Assert.Equal(0, await _gracePass.RunAsync(P.AddDays(14), default));
        Assert.Equal(_proId, await UserPlanIdAsync());
        Assert.Equal(0, GraceEndedEmailCount());
        Assert.Equal(0, PlanChangedGraceCount());
    }

    [Fact]
    public async Task Us01906_ResumeAfterDowngrade_RestoresPro_NoSecondDowngrade()
    {
        await PauseAsync("evt_pause", P);
        Assert.Equal(1, await _gracePass.RunAsync(P.AddDays(14), default));
        Assert.Equal(_freeId, await UserPlanIdAsync());

        var result = await _stripe.ApplySubscriptionAsync(
            Change("evt_resume", "customer.subscription.resumed", "active"), _proId, P.AddDays(15), default);

        Assert.Equal(StripeApplyOutcome.Applied, result.Outcome);
        Assert.Equal("free", result.FromPlanCode);
        Assert.Equal("pro", result.ToPlanCode);
        Assert.Equal(_proId, await UserPlanIdAsync());
        Assert.Null((await SubAsync()).GraceEndsAtUtc);

        Assert.Equal(0, await _gracePass.RunAsync(P.AddDays(30), default));
        Assert.Equal(_proId, await UserPlanIdAsync());
        Assert.Equal(1, GraceEndedEmailCount());
    }

    [Fact]
    public async Task Us01906_DuplicateResumed_IsDuplicate_NoChange()
    {
        await PauseAsync("evt_pause", P);

        var first = await _stripe.ApplySubscriptionAsync(
            Change("evt_resume", "customer.subscription.resumed", "active"), _proId, P.AddDays(2), default);
        var replay = await _stripe.ApplySubscriptionAsync(
            Change("evt_resume", "customer.subscription.resumed", "active"), _proId, P.AddDays(3), default);

        Assert.Equal(StripeApplyOutcome.Applied, first.Outcome);
        Assert.Equal(StripeApplyOutcome.Duplicate, replay.Outcome);
        Assert.Null((await SubAsync()).GraceEndsAtUtc);
        Assert.Equal(_proId, await UserPlanIdAsync());
    }

    [Fact]
    public async Task Us01906_ResumeWithoutPause_AlreadyPro_NoOp()
    {
        var result = await _stripe.ApplySubscriptionAsync(
            Change("evt_resume", "customer.subscription.resumed", "active"), _proId, P, default);

        Assert.Equal(StripeApplyOutcome.Applied, result.Outcome);
        Assert.Equal(_proId, result.FromPlanId);
        Assert.Equal(_proId, result.ToPlanId);
        Assert.Null((await SubAsync()).GraceEndsAtUtc);
    }

    [Fact]
    public async Task SecondPausedEvent_DoesNotExtendGrace_SameEventId_IsDuplicate()
    {
        await PauseAsync("evt_pause", P);

        var second = await _stripe.ApplySubscriptionAsync(
            Change("evt_pause_2", "customer.subscription.paused", "paused"), null, P.AddDays(2), default);
        var replay = await _stripe.ApplySubscriptionAsync(
            Change("evt_pause", "customer.subscription.paused", "paused"), null, P.AddDays(3), default);

        Assert.Equal(StripeApplyOutcome.Applied, second.Outcome);
        Assert.Equal(StripeApplyOutcome.Duplicate, replay.Outcome);
        Assert.Equal(P.AddDays(14), (await SubAsync()).GraceEndsAtUtc);
    }
}
