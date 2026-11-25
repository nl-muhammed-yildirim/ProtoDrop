namespace wa.application.UseCases.DataExport;

/// <summary>One owned transfer in transfers.json (FR-047-1, AC-047-3).</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="CreatedAtUtc">UTC instant the transfer was created.</param>
/// <param name="Status">Stored transfer status byte (see <c>TransferStatus</c>).</param>
/// <param name="ExpiresAtUtc">UTC instant the transfer expires.</param>
/// <param name="PasswordSet">True when the transfer is password protected; the password itself is never exported.</param>
/// <param name="DownloadsCount">Downloads served so far.</param>
/// <param name="Files">Names and sizes of the transfer's files.</param>
/// <param name="Recipients">Addresses the transfer was emailed to; PII (TA-9.4).</param>
public sealed record ExportTransfer(
    Guid Id,
    DateTime CreatedAtUtc,
    byte Status,
    DateTime ExpiresAtUtc,
    bool PasswordSet,
    int DownloadsCount,
    IReadOnlyList<ExportFile> Files,
    IReadOnlyList<string> Recipients);
