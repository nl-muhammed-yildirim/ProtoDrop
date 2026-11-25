using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/draft result (TA-4.2#1 response shape — the endpoint re-flattens this into its anonymous body).</summary>
/// <param name="DraftId">Id of the new draft; the upload blobs are staged under it.</param>
/// <param name="ExpiresInSec">Seconds the upload URLs stay valid (two hours).</param>
/// <param name="Files">One upload URL per requested file, in request order.</param>
public sealed record CreateDraftResult(Guid DraftId, int ExpiresInSec, IReadOnlyList<DraftFile> Files);
