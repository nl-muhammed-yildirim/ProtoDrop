namespace wa.application.unit;

/// <summary>
/// CHORE-code-style-2: hand-written <see cref="TimeProvider"/> test double (no Microsoft.Extensions.TimeProvider.Testing package, TA-17).
/// Returns a settable instant so a test can pin <c>GetUtcNow()</c> and assert the exact value a handler read from its injected clock.
/// </summary>
/// <param name="utcNow">The instant the clock starts at.</param>
public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    /// <summary>Gets or sets the instant returned by <see cref="GetUtcNow"/>.</summary>
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    /// <summary>Moves the clock forward by <paramref name="by"/>.</summary>
    /// <param name="by">The amount of time to add to <see cref="UtcNow"/>.</param>
    public void Advance(TimeSpan by) => UtcNow += by;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
