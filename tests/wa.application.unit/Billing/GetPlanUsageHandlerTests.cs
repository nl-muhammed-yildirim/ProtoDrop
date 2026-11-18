using wa.application.Limits;
using wa.application.UseCases.Billing;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-020-03 (AC-020-3): GetPlanUsageHandler maps PlanContext limits + live usage reads.</summary>
public class GetPlanUsageHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static LimitsRecord Limits(long storageQuota, int activeMax) =>
        new() { StorageQuota = storageQuota, ActiveTransfersMax = activeMax };

    [Fact]
    public async Task Handle_FreeContext_MapsLimitsAndLiveUsage()
    {
        var store = new StubTransferStore { Bytes = 1234, Count = 3 };
        var handler = new GetPlanUsageHandler(new StubPlanContext("free", Limits(5368709120L, 20)), store);

        var r = await handler.Handle(new GetPlanUsageQuery(UserId), default);

        Assert.Equal("free", r.PlanCode);
        Assert.Equal(1234, r.StorageUsedBytes);
        Assert.Equal(5368709120L, r.StorageAllowedBytes);
        Assert.Equal(3, r.ActiveTransfersUsed);
        Assert.Equal(20, r.ActiveTransfersAllowed);
        Assert.Equal(UserId, store.BytesOwner);
        Assert.Equal(UserId, store.CountOwner);
    }

    [Fact]
    public async Task Handle_InfiniteActiveTransfers_AllowedNull_UsedStillCounted()
    {
        var store = new StubTransferStore { Bytes = 10, Count = 500 };
        var handler = new GetPlanUsageHandler(new StubPlanContext("business", Limits(1099511627776L, -1)), store);

        var r = await handler.Handle(new GetPlanUsageQuery(UserId), default);

        Assert.Null(r.ActiveTransfersAllowed);
        Assert.Equal(500, r.ActiveTransfersUsed);
    }

    [Fact]
    public async Task Handle_OverQuotaAfterDowngrade_ReturnsRawUsedAboveAllowed()
    {
        var store = new StubTransferStore { Bytes = 0, Count = 100 };
        var handler = new GetPlanUsageHandler(new StubPlanContext("free", Limits(5368709120L, 20)), store);

        var r = await handler.Handle(new GetPlanUsageQuery(UserId), default);

        Assert.Equal(100, r.ActiveTransfersUsed);
        Assert.Equal(20, r.ActiveTransfersAllowed);
    }

    private sealed class StubPlanContext(string code, LimitsRecord limits) : IPlanContextAccessor
    {
        public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanContext(code, limits, new PlanFeatures()));
    }

    private sealed class StubTransferStore : ITransferStore
    {
        public long Bytes { get; set; }
        public int Count { get; set; }
        public Guid? BytesOwner { get; private set; }
        public Guid? CountOwner { get; private set; }

        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) { BytesOwner = ownerId; return Task.FromResult(Bytes); }
        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) { CountOwner = ownerId; return Task.FromResult(Count); }

        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct) => throw new NotSupportedException();
        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }
}
