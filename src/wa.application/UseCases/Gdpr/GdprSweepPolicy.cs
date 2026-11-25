namespace wa.application.UseCases.Gdpr;

/// <summary>US-047-02: architecture constants of the sweep (TA-6.3 style, not Limits Registry values).</summary>
public static class GdprSweepPolicy
{
    /// <summary>A marker becomes due this long after the deletion.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(24);

    /// <summary>Telemetry retention (TA-11.2, wa-prod-ai 90 d) — the sweep scope.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <summary>Markers handled per run (TA-6.3 batch).</summary>
    public const int BatchSize = 500;
}
