namespace wa.application.UseCases.DataExport;

/// <summary>One owned transfer in transfers.json (FR-047-1, AC-047-3).</summary>
public sealed record ExportTransfer(
    Guid Id,
    DateTime CreatedAtUtc,
    byte Status,
    DateTime ExpiresAtUtc,
    bool PasswordSet,
    int DownloadsCount,
    IReadOnlyList<ExportFile> Files,
    IReadOnlyList<string> Recipients);
