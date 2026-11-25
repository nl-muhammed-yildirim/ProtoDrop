namespace wa.application.UseCases.DataExport;

/// <summary>One file's name and size in transfers.json.</summary>
/// <param name="Name">Original file name.</param>
/// <param name="SizeBytes">File size in bytes.</param>
public sealed record ExportFile(string Name, long SizeBytes);
