namespace wa.application.Limits;

/// <summary>Raw flag row as read from the <c>FeatureFlag</c> table (TA-3.4 port boundary).</summary>
public sealed record FlagRow(string Key, string Value);

/// <summary>
/// TA-3.4 — loads the SQL <c>FeatureFlag</c> rows (port boundary, TA-0.2 rule 7:
/// no <c>DbContext</c> in domain — the provider keeps DB reads in
/// infrastructure/application). One read per refresh.
/// </summary>
public interface IFlagsStore
{
    Task<IReadOnlyList<FlagRow>> GetFlagsAsync(CancellationToken ct);
}

/// <summary>
/// TA-3.4 — in-memory <c>FeatureFlag</c> cache, 30 s TTL (Part 2 technical
/// constant). TTL honored: after <see cref="Ttl"/> the next read re-queries
/// the source store. Thread-safe for concurrent readers.
/// </summary>
public sealed class FlagsCache
{
    /// <summary>TTL per TA-3.4 / Open-Decisions-and-Constants.md Part 2.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    readonly IFlagsStore _source;
    readonly Func<DateTimeOffset> _now;
    readonly TimeSpan _ttl;

    DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    IReadOnlyList<FlagRow>? _rows;

    public FlagsCache(IFlagsStore source, Func<DateTimeOffset>? now = null, TimeSpan? ttl = null)
    {
        _source = source;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? Ttl;
    }

    /// <summary>Get the flag rows, refreshing when the TTL has expired.</summary>
    public async Task<IReadOnlyList<FlagRow>> GetFlagsAsync(CancellationToken ct = default)
    {
        if (_rows is null || _now() - _fetchedAt >= _ttl)
        {
            _rows = await _source.GetFlagsAsync(ct);
            _fetchedAt = _now();
        }

        return _rows;
    }
}