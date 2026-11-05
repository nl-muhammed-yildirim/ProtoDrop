using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04 (TA-4.2#21 / FR-011-3): GET /api/v1/admin/transfers — platform-wide admin transfer search, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim/null, status vocabulary resolution, range validation, cursor decode, then one store call.
/// </summary>
public sealed record SearchTransfersQuery(
    string? Q,
    string? Status,
    long? MinBytes,
    long? MaxBytes,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Cursor,
    int Limit) : IRequest<SearchTransfersResult>;

/// <summary>T-011-04: flat result — items + nextCursor (null when no next page).</summary>
public record SearchTransfersResult(
    IReadOnlyList<AdminTransferRow> Items,
    string? NextCursor);

/// <summary>
/// T-011-04: validation failure inside the search use case — same pattern as LoginValidationException (T-008-02).
/// The endpoint catches and maps to VALIDATION (400) via the closed code list (TA-4.1.3); no new code added.
/// </summary>
public sealed class SearchTransfersValidationException : Exception
{
    public SearchTransfersValidationException(string message) : base(message) { }
}

/// <summary>
/// T-011-04: handler — pure composition over ISearchTransfersStore.
/// (1) q = Q?.Trim(); null/empty → null;
/// (2) status vocabulary resolved here: all→null, draft→{0}, active→{1}, expired→{2}, download_limit→{3}, deleted→{4}; absent→null (all); unknown→BusinessException;
/// (3) from/to and minBytes/maxBytes each require BOTH or NEITHER — exactly one present → BusinessException;
/// (4) cursor decoded via CursorCodec.Decode (malformed → rethrow CursorFormatException; endpoint maps to 400 VALIDATION);
/// (5) call store once with the resolved args + limit.
/// </summary>
public sealed class SearchTransfersQueryHandler(ISearchTransfersStore store) : IRequestHandler<SearchTransfersQuery, SearchTransfersResult>
{
    public async Task<SearchTransfersResult> Handle(SearchTransfersQuery request, CancellationToken cancellationToken)
    {
        // (1) q: trim and null out empty/whitespace-only.
        var q = request.Q?.Trim();
        if (string.IsNullOrEmpty(q)) q = null;

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

        // (5) call store once with resolved args + limit.
        var (items, nextCursor) = await store.SearchAsync(
            q,
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
