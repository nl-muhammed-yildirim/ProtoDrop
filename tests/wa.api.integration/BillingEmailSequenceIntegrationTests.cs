using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using wa.application.UseCases.Billing;
using wa.domain;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-019-04 (AC-019-2 first half, EC-019-2, duplicate invoice.payment_failed): the real SQL ledger and the f-expire sub-step,
/// time-travelled with explicit nowUtc. SQL 2022 Testcontainers only — no Azurite, no HTTP host.
/// </summary>
public class BillingEmailSequenceIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string Email = "payer@example.com";

    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private BillingSequenceStore _store = null!;
    private InMemoryEventPublisher _events = null!;
    private SendBillingEmailsHandler _pass = null!;
    private Guid _userId;
    private Guid _subId;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();

        var options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(options);
        await _db.Database.MigrateAsync();

        var freeId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Billing Test",
            PlanId = freeId,
            Theme = "system",
            CreatedAtUtc = T0
        });
        _subId = Guid.NewGuid();
        _db.Set<Subscription>().Add(new Subscription
        {
            Id = _subId,
            AppUserId = _userId,
            StripeSubId = "sub_it",
            StripeCustomerId = "cus_it",
            PlanCode = "pro",
            Status = "active",
            CreatedAtUtc = T0,
            UpdatedAtUtc = T0
        });
        await _db.SaveChangesAsync();

        _store = new BillingSequenceStore(new AppDbContextFactory(options));
        _events = new InMemoryEventPublisher();
        _pass = new SendBillingEmailsHandler(_store, _events, NullLogger<SendBillingEmailsHandler>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private async Task<string> SubStatusAsync() =>
        (await _db.Set<Subscription>().AsNoTracking().SingleAsync()).Status;

    private async Task<int> JobRunCountAsync() =>
        await _db.Set<JobRun>().AsNoTracking().CountAsync(j => j.JobKey.StartsWith("billing-pf:"));

    [Fact]
    public async Task FullSequence_0_3_7_Days_ThenNoMore()
    {
        var started = await _store.StartPaymentFailedAsync("evt_1", "in_1", "sub_it", T0, default);
        Assert.Equal(PaymentFailedOutcome.Started, started.Outcome);
        Assert.Equal(Email, started.Email);
        Assert.Equal("past_due", await SubStatusAsync());

        Assert.Equal(0, await _pass.RunAsync(T0.AddDays(1), default));
        Assert.Equal(1, await _pass.RunAsync(T0.AddDays(3), default));   // step 2
        Assert.Equal(0, await _pass.RunAsync(T0.AddDays(3), default));   // no double-send
        Assert.Equal(1, await _pass.RunAsync(T0.AddDays(7), default));   // step 3
        Assert.Equal(0, await _pass.RunAsync(T0.AddDays(8), default));

        Assert.Equal(3, await JobRunCountAsync());
        var steps = _events.GetByTopic("core")
            .Select(e => JsonDocument.Parse(e.Json).RootElement.GetProperty("payload").GetProperty("step").GetInt32())
            .ToList();
        Assert.Equal(2, steps.Count); // the pass queued #2 and #3; #1 is queued by the webhook handler from StartPaymentFailed
        Assert.Contains(2, steps);
        Assert.Contains(3, steps);
    }

    [Fact]
    public async Task InvoicePaid_OnDay2_CancelsRemainingEmails_AndRestoresActive()
    {
        await _store.StartPaymentFailedAsync("evt_1", "in_1", "sub_it", T0, default);

        var outcome = await _store.CancelSequenceAsync("evt_paid", "sub_it", T0.AddDays(2), default);

        Assert.Equal(InvoicePaidOutcome.Cancelled, outcome);
        Assert.Equal("active", await SubStatusAsync());
        Assert.Equal(0, await _pass.RunAsync(T0.AddDays(3), default));
        Assert.Equal(0, await _pass.RunAsync(T0.AddDays(7), default));
        Assert.Empty(_events.GetByTopic("core"));
    }

    [Fact]
    public async Task DuplicateInvoice_NewEventId_IsDuplicateInvoice_OneStep1Row_SameEventId_IsDuplicateEvent()
    {
        var first = await _store.StartPaymentFailedAsync("evt_1", "in_1", "sub_it", T0, default);
        var smartRetry = await _store.StartPaymentFailedAsync("evt_2", "in_1", "sub_it", T0.AddHours(12), default);
        var redelivery = await _store.StartPaymentFailedAsync("evt_1", "in_1", "sub_it", T0.AddHours(13), default);

        Assert.Equal(PaymentFailedOutcome.Started, first.Outcome);
        Assert.Equal(PaymentFailedOutcome.DuplicateInvoice, smartRetry.Outcome);
        Assert.Equal(PaymentFailedOutcome.DuplicateEvent, redelivery.Outcome);
        var step1Key = BillingEmailSchedule.StepJobKey(_subId, 1);
        Assert.Equal(1, await _db.Set<JobRun>().AsNoTracking().CountAsync(j => j.JobKey == step1Key));
    }

    [Fact]
    public async Task CanceledSubscription_IsIneligible_NoJobRun_EventRowKept()
    {
        var sub = await _db.Set<Subscription>().SingleAsync();
        sub.Status = "canceled";
        await _db.SaveChangesAsync();

        var result = await _store.StartPaymentFailedAsync("evt_c", "in_c", "sub_it", T0, default);

        Assert.Equal(PaymentFailedOutcome.Ineligible, result.Outcome);
        Assert.Equal(0, await JobRunCountAsync());
        Assert.True(await _db.Set<StripeEvent>().AsNoTracking().AnyAsync(e => e.StripeEventId == "evt_c"));
        Assert.Equal("canceled", await SubStatusAsync());
    }

    [Fact]
    public async Task UnknownSubscription_IsUnresolved_NoStripeEventRowKept()
    {
        var result = await _store.StartPaymentFailedAsync("evt_u", "in_u", "sub_missing", T0, default);

        Assert.Equal(PaymentFailedOutcome.SubscriptionUnresolved, result.Outcome);
        Assert.False(await _db.Set<StripeEvent>().AsNoTracking().AnyAsync(e => e.StripeEventId == "evt_u"));
    }
}
