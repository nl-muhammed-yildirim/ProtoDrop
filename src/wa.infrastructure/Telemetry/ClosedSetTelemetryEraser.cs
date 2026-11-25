using wa.application.UseCases.Gdpr;

namespace wa.infrastructure.Telemetry;

/// <summary>
/// US-047-02 (FR-047-5), option A "contract by construction" (decided 2026-11-18): completes immediately without calling any purge API.
/// Coupling: a marker being "swept" means no userId-keyed custom event exists, because the telemetry closed sets (rules R3/R5 in
/// scripts/check-telemetry-pii.mjs, CI job pii-check) keep userId out of every event. If a future change adds userId to a telemetry
/// property, this adapter must be replaced by a real purge behind <see cref="ITelemetryEraser"/> (handler, store and table stay).
/// </summary>
public sealed class ClosedSetTelemetryEraser : ITelemetryEraser
{
    /// <inheritdoc />
    public Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct) => Task.CompletedTask;
}
