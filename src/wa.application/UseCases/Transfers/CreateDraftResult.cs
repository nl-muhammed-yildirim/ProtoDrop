using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/draft result (TA-4.2#1 response shape — the endpoint re-flattens this into its anonymous body).</summary>
/// <param name="DraftId">Identifier of the draft of the create draft result.</param>
/// <param name="ExpiresInSec">The expires in sec of the create draft result.</param>
/// <param name="Files">The files of the create draft result.</param>
public sealed record CreateDraftResult(Guid DraftId, int ExpiresInSec, IReadOnlyList<DraftFile> Files);
