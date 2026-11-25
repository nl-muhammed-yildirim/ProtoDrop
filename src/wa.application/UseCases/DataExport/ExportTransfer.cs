namespace wa.application.UseCases.DataExport;

/// <summary>One owned transfer in transfers.json (FR-047-1, AC-047-3).</summary>
/// <param name="Id">Identifier of the export transfer.</param>
/// <param name="CreatedAtUtc">The created at UTC of the export transfer.</param>
/// <param name="Status">The status of the export transfer.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the export transfer.</param>
/// <param name="PasswordSet">Whether password set of the export transfer.</param>
/// <param name="DownloadsCount">The downloads count of the export transfer.</param>
/// <param name="Files">The files of the export transfer.</param>
/// <param name="Recipients">The recipients of the export transfer.</param>
public sealed record ExportTransfer(
    Guid Id,
    DateTime CreatedAtUtc,
    byte Status,
    DateTime ExpiresAtUtc,
    bool PasswordSet,
    int DownloadsCount,
    IReadOnlyList<ExportFile> Files,
    IReadOnlyList<string> Recipients);
