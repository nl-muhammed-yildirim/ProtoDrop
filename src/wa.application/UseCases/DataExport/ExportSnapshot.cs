namespace wa.application.UseCases.DataExport;

/// <summary>Everything the zip builder needs.</summary>
/// <param name="Profile">The profile of the export snapshot.</param>
/// <param name="Transfers">The transfers of the export snapshot.</param>
public sealed record ExportSnapshot(ExportProfile Profile, IReadOnlyList<ExportTransfer> Transfers);
