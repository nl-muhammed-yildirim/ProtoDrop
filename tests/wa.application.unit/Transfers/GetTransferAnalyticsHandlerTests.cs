using wa.application.Limits;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Analytics;

namespace wa.application.unit.Transfers;

/// <summary>US-023-01 (FR-023-1/5): handler order makes "no query ran" structural for Free viewers.</summary>
public class GetTransferAnalyticsHandlerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid TransferId = Guid.NewGuid();

    private static LimitsRecord Limits() => new() { RetentionDays = 30, GraceDays = 3 };

    private static GetTransferAnalyticsHandler Handler(bool analytics, StubStore store) =>
        new(new StubPlanContext(Limits(), new PlanFeatures { Analytics = analytics }), store);

    [Fact]
    public async Task Handle_FreePlan_ThrowsForbidden_NoStoreCall()
    {
        var store = new StubStore();

        await Assert.ThrowsAsync<AnalyticsForbiddenException>(() =>
            Handler(false, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default));

        Assert.Equal(0, store.OwnedCalls);
        Assert.Equal(0, store.AggregateCalls);
    }

    [Fact]
    public async Task Handle_ProPlan_NotOwned_ReturnsNull_NoAggregate()
    {
        var store = new StubStore { Transfer = null };

        var r = await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        Assert.Null(r);
        Assert.Equal(1, store.OwnedCalls);
        Assert.Equal(0, store.AggregateCalls);
        Assert.Equal((OwnerId, TransferId), store.LastOwnedArgs);
    }

    [Fact]
    public async Task Handle_ProPlan_WithEvents_ReturnsDailyTopCountryTopBrowserTotal()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var store = new StubStore
        {
            Transfer = new AnalyticsTransfer(TransferId, DateTime.UtcNow.AddDays(-5), 1),
            Aggregate = new AnalyticsAggregate(
                [new DailyCount(today, 3), new DailyCount(today.AddDays(-2), 1)],
                [("NL", 3), ("DE", 1)],
                [("Chrome", 3), ("Firefox", 1)],
                [],
                [])
        };

        var r = await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        Assert.NotNull(r);
        Assert.Equal(4, r!.Total);
        Assert.Equal(new BucketCount("NL", 3), r.Countries[0]);
        Assert.Equal(new BucketCount("Chrome", 3), r.Browsers[0]);
        Assert.Equal(today, r.Daily[^1].Day);
        Assert.Equal(3, r.Daily[^1].Count);
    }

    [Fact]
    public async Task Handle_ProPlan_ZeroEvents_TotalZero_DailyAllZero_EmptyBuckets()
    {
        var store = new StubStore { Transfer = new AnalyticsTransfer(TransferId, DateTime.UtcNow.AddDays(-2), 1) };

        var r = await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        Assert.NotNull(r);
        Assert.Equal(0, r!.Total);
        Assert.NotEmpty(r.Daily);
        Assert.All(r.Daily, d => Assert.Equal(0, d.Count));
        Assert.Empty(r.Countries);
        Assert.Empty(r.Browsers);
    }

    [Fact]
    public async Task Handle_SinceUtc_IsWindowStartFromPlanLimits()
    {
        var store = new StubStore { Transfer = new AnalyticsTransfer(TransferId, DateTime.UtcNow.AddDays(-400), 1) };

        await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        var expected = DateTime.UtcNow.Date.AddDays(-32);
        // Tolerate a midnight rollover between the handler's clock read and this one.
        Assert.True(store.LastSinceUtc == expected || store.LastSinceUtc == expected.AddDays(1), $"since = {store.LastSinceUtc:o}");
        Assert.Equal(DateTimeKind.Utc, store.LastSinceUtc.Kind);
    }

    [Fact]
    public async Task Handle_SinceUtc_RecentTransfer_IsCreatedDate()
    {
        var created = DateTime.UtcNow.AddDays(-3);
        var store = new StubStore { Transfer = new AnalyticsTransfer(TransferId, created, 1) };

        await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        Assert.Equal(created.Date, store.LastSinceUtc);
    }

    [Fact]
    public async Task Handle_ProPlan_PassesFileCountsThrough()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new StubStore
        {
            Aggregate = new AnalyticsAggregate(
                [], [], [],
                [new AnalyticsFile(a, "a.pdf", 0), new AnalyticsFile(b, "b.pdf", 1)],
                [new FileCountRow(b, 4, now), new FileCountRow(a, 1, now), new FileCountRow(null, 2, now)])
        };

        var r = await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        Assert.NotNull(r);
        Assert.Equal(2, r!.Files.Files.Count);
        Assert.Equal(2, r.Files.DownloadAll);
        Assert.Equal(b, r.Files.Top[0].FileId);
    }

    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public async Task Handle_ExpiredTransfer_StillReturnsData(byte status)
    {
        var store = new StubStore { Transfer = new AnalyticsTransfer(TransferId, DateTime.UtcNow.AddDays(-10), status) };

        var r = await Handler(true, store).Handle(new GetTransferAnalyticsQuery(TransferId, OwnerId), default);

        Assert.NotNull(r);
        Assert.Equal(1, store.AggregateCalls);
    }

    private sealed class StubPlanContext(LimitsRecord limits, PlanFeatures features) : IPlanContextAccessor
    {
        public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanContext("pro", limits, features));
    }

    private sealed class StubStore : ITransferAnalyticsStore
    {
        public AnalyticsTransfer? Transfer { get; set; } = new(TransferId, DateTime.UtcNow.AddDays(-1), 1);
        public AnalyticsAggregate Aggregate { get; set; } = new([], [], [], [], []);
        public int OwnedCalls { get; private set; }
        public int AggregateCalls { get; private set; }
        public (Guid Owner, Guid Transfer) LastOwnedArgs { get; private set; }
        public DateTime LastSinceUtc { get; private set; }

        public Task<AnalyticsTransfer?> GetOwnedTransferAsync(Guid ownerId, Guid transferId, CancellationToken ct)
        {
            OwnedCalls++;
            LastOwnedArgs = (ownerId, transferId);
            return Task.FromResult(Transfer);
        }

        public Task<AnalyticsAggregate> AggregateAsync(Guid transferId, DateTime sinceUtc, CancellationToken ct)
        {
            AggregateCalls++;
            LastSinceUtc = sinceUtc;
            return Task.FromResult(Aggregate);
        }
    }
}
