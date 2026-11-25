namespace wa.application.UseCases.DataExport;

/// <summary>One file's name and size in transfers.json.</summary>
/// <param name="Name">The name of the export file.</param>
/// <param name="SizeBytes">The size bytes of the export file.</param>
public sealed record ExportFile(string Name, long SizeBytes);
