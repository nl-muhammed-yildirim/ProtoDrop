namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-009-03: the owner-detail payload. COMPOSES T-009-01's MyTransferRow (single source of truth for the row fields —
/// no duplicated flat copy) and adds only the per-file rows + recipient addresses. Null from the handler = not found
/// / foreign owner / guest / deleted — ONE indistinguishable case (endpoint maps null → 404 NOT_FOUND; no FORBIDDEN).
/// </summary>
/// <param name="Row">The list-row fields of the transfer.</param>
/// <param name="Files">Names and sizes of the transfer's files.</param>
/// <param name="Recipients">Addresses the transfer was emailed to; PII (TA-9.4).</param>
public sealed record GetMyTransferResult(MyTransferRow Row, IReadOnlyList<GetMyTransferFile> Files, IReadOnlyList<string> Recipients);
