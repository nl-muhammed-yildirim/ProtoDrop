using Microsoft.Extensions.Logging;
using wa.domain;
using wa.application.UseCases.Transfers;

namespace wa.infrastructure.Persistence;

/// <summary>T-006-03 (FR-006-5): Unsubscribe result — Success or NotFound.</summary>
public record UnsubscribeResult(bool Success);
