using wa.application.UseCases.EmailSettings;

namespace wa.application.unit.EmailSettings;

/// <summary>F-XCT-003 (TA-2.3): hand-rolled IEmailNotificationsStore stub — records writes and serves stored values.</summary>
public sealed class StubEmailNotificationsStore : IEmailNotificationsStore
{
    public Dictionary<Guid, bool?> Stored { get; } = [];
    public List<(Guid UserId, bool Enabled)> Writes { get; } = [];
    public int Reads { get; private set; }

    /// <summary>Invoked after each write — lets tests mirror the column onto a domain object.</summary>
    public Action<Guid, bool>? OnSet { get; set; }

    public Task SetAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        Writes.Add((userId, enabled));
        Stored[userId] = enabled;
        OnSet?.Invoke(userId, enabled);
        return Task.CompletedTask;
    }

    public Task<bool?> GetAsync(Guid userId, CancellationToken ct)
    {
        Reads++;
        return Task.FromResult(Stored.GetValueOrDefault(userId));
    }
}
