using MediatR;
using wa.application.Limits;
using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>US-023-01 (TA-4.2#24, FR-023-1/5): owner-scoped download analytics for one transfer. Null = not found (404).</summary>
public sealed record GetTransferAnalyticsQuery(Guid Id, Guid OwnerId) : IRequest<TransferAnalytics?>;

/// <summary>US-023-01 (FR-023-5): the viewer's plan (or the global feature.analytics gate) does not include analytics.
/// The endpoint maps it to 403 FORBIDDEN, keeping the closed code list intact.</summary>
public sealed class AnalyticsForbiddenException : Exception
{
    public AnalyticsForbiddenException() : base("Pro plan required.") { }
}

public sealed class GetTransferAnalyticsHandler : IRequestHandler<GetTransferAnalyticsQuery, TransferAnalytics?>
{
    private readonly IPlanContextAccessor _planContext;
    private readonly ITransferAnalyticsStore _store;

    public GetTransferAnalyticsHandler(IPlanContextAccessor planContext, ITransferAnalyticsStore store)
    {
        _planContext = planContext ?? throw new ArgumentNullException(nameof(planContext));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<TransferAnalytics?> Handle(GetTransferAnalyticsQuery request, CancellationToken ct)
    {
        // Gate first: a Free viewer never reaches the store (AC-023-3, EC-023-6).
        var plan = await _planContext.GetAsync(ct);
        if (!plan.Features.Analytics) throw new AnalyticsForbiddenException();

        var owned = await _store.GetOwnedTransferAsync(request.OwnerId, request.Id, ct);
        if (owned is null) return null;   // expired (2) / DownloadLimit (3) rows are still returned (EC-023-5)

        var now = DateTime.UtcNow;
        var from = DownloadAnalytics.WindowStart(now, owned.CreatedAtUtc, plan.Limits.RetentionDays, plan.Limits.GraceDays);
        var to = DateOnly.FromDateTime(now);
        var sinceUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var agg = await _store.AggregateAsync(request.Id, sinceUtc, ct);
        return DownloadAnalytics.Build(agg.Daily, agg.Countries, agg.Browsers, from, to);
    }
}
