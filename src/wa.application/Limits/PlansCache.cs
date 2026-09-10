namespace wa.application.Limits;

/// <summary>Raw plan row as read from the <c>Plan</c> table (TA-3.4 port boundary).</summary>
public sealed record PlanRow(Guid Id, string Code, string Name, string LimitsJson, string FeaturesJson, int SortOrder);

/// <summary>
/// TA-3.4 — loads SQL <c>Plan</c> rows (port boundary, TA-0.2 rule 7).
/// One read per refresh.
/// </summary>
public interface IPlansStore
{
    Task<IReadOnlyList<PlanRow>> GetPlansAsync(CancellationToken ct);
}

/// <summary>
/// TA-3.4 — in-memory <c>Plan</c> cache (incl. <c>LimitsJson</c>), 30 s TTL
/// (Part 2 technical constant). TTL honored: after <see cref="Ttl"/> the
/// next read re-queries the source store. Thread-safe for concurrent readers.
/// </summary>
public sealed class PlansCache
{
    /// <summary>TTL per TA-3.4 / Open-Decisions-and-Constants.md Part 2.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    readonly IPlansStore _source;
    readonly Func<DateTimeOffset> _now;
    readonly TimeSpan _ttl;

    DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    IReadOnlyList<PlanRow>? _rows;

    public PlansCache(IPlansStore source, Func<DateTimeOffset>? now = null, TimeSpan? ttl = null)
    {
        _source = source;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? Ttl;
    }

    /// <summary>Get the plan rows, refreshing when the TTL has expired.</summary>
    public async Task<IReadOnlyList<PlanRow>> GetPlansAsync(CancellationToken ct = default)
    {
        if (_rows is null || _now() - _fetchedAt >= _ttl)
        {
            _rows = await _source.GetPlansAsync(ct);
            _fetchedAt = _now();
        }

        return _rows;
    }
}