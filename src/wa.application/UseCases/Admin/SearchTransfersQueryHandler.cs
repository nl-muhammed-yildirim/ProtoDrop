using MediatR;
using wa.application.Limits;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04: handler — pure composition over ISearchTransfersStore.
/// (1) q: US-045-01 (FR-045-4) — with feature.search on, SearchTerm.Normalize (Q_TOO_LONG rule + widened file/sender match); off → legacy trim/null;
/// (2) status vocabulary resolved here: all→null, draft→{0}, active→{1}, expired→{2}, download_limit→{3}, deleted→{4}; absent→null (all); unknown→BusinessException;
/// (3) from/to and minBytes/maxBytes each require BOTH or NEITHER — exactly one present → BusinessException;
/// (4) cursor decoded via CursorCodec.Decode (malformed → rethrow CursorFormatException; endpoint maps to 400 VALIDATION);
/// (5) call store once with the resolved args + limit.
/// </summary>
public sealed class SearchTransfersQueryHandler(ISearchTransfersStore store, IFlagResolver flags) : IRequestHandler<SearchTransfersQuery, SearchTransfersResult>
{
    public async Task<SearchTransfersResult> Handle(SearchTransfersQuery request, CancellationToken cancellationToken)
    {
        // (1) q. US-045-01 (FR-045-4): with feature.search on, q follows the FR-045-1 term rules and also matches file + sender names.
        var searchOn = await flags.ResolveFlag(FeatureGates.Search, FeatureGates.DefaultWhenAbsent, cancellationToken);
        string? q;
        if (searchOn)
            q = SearchTerm.Normalize(request.Q);
        else
        {
            q = request.Q?.Trim();                     // FR-011-3 legacy behaviour, unchanged when the gate is off
            if (string.IsNullOrEmpty(q)) q = null;
        }

        // (2) status vocabulary resolution.
        IReadOnlyCollection<byte>? statuses = ResolveStatus(request.Status);

        // (3) range validation: from/to and minBytes/maxBytes each require BOTH or NEITHER.
        if ((request.FromUtc is null) != (request.ToUtc is null))
            throw new SearchTransfersValidationException("from and to must both be provided or both omitted.");
        if ((request.MinBytes is null) != (request.MaxBytes is null))
            throw new SearchTransfersValidationException("minBytes and maxBytes must both be provided or both omitted.");

        // (4) cursor decode — malformed → CursorFormatException (endpoint maps to 400 VALIDATION).
        DateTime? afterUtc = null;
        Guid? afterIdExcluded = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var (createdAtUtc, id) = CursorCodec.Decode(request.Cursor);
            afterUtc = createdAtUtc;
            afterIdExcluded = id;
        }

        // (5) call store once with resolved args + limit. matchFilesAndSender = gate on → widened file/sender match (FR-045-4).
        var (items, nextCursor) = await store.SearchAsync(
            q,
            searchOn,
            statuses,
            request.MinBytes,
            request.MaxBytes,
            request.FromUtc,
            request.ToUtc,
            afterUtc,
            afterIdExcluded,
            request.Limit,
            cancellationToken);

        return new SearchTransfersResult(items, nextCursor);
    }

    /// <summary>
    /// T-011-04: status vocabulary — per-status values (draft/active/expired/download_limit/deleted), NOT T-009-02's grouping.
    /// absent→null (all); all→null; unknown→BusinessException(ProblemCodes.Validation).
    /// </summary>
    private static IReadOnlyCollection<byte>? ResolveStatus(string? status)
    {
        if (status is null) return null; // absent → all
        return status.Trim().ToLowerInvariant() switch
        {
            "all" => null,
            "draft" => new byte[] { 0 },
            "active" => new byte[] { 1 },
            "expired" => new byte[] { 2 },
            "download_limit" => new byte[] { 3 },
            "deleted" => new byte[] { 4 },
            _ => throw new SearchTransfersValidationException("status must be one of all / draft / active / expired / download_limit / deleted.")
        };
    }
}
