using wa.domain;

namespace wa.application.Ports;

/// <summary>Reads flag rows. Limit resolution consumes the raw rows — key filtering and override layering live in T-053-04.</summary>
public interface IFlagsStore
{
    /// <summary>Returns all seeded FeatureFlag rows (keys + string values, unfiltered).</summary>
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken);
}
