namespace wa.application.UseCases.DataExport;

/// <summary>Everything the zip builder needs.</summary>
public sealed record ExportSnapshot(ExportProfile Profile, IReadOnlyList<ExportTransfer> Transfers);
