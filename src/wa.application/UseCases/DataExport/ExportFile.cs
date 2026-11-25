namespace wa.application.UseCases.DataExport;

/// <summary>One file's name and size in transfers.json.</summary>
public sealed record ExportFile(string Name, long SizeBytes);
