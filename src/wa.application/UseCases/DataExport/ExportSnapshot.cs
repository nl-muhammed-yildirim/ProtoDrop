namespace wa.application.UseCases.DataExport;

/// <summary>Everything the zip builder needs: the profile and the user's own transfers (metadata only).</summary>
/// <param name="Profile">The account profile.</param>
/// <param name="Transfers">The user's transfers; empty when the user has none.</param>
public sealed record ExportSnapshot(ExportProfile Profile, IReadOnlyList<ExportTransfer> Transfers);
