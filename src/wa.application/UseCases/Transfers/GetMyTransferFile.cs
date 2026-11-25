namespace wa.application.UseCases.Transfers;

/// <summary>One per-file row of the My Files detail (FR-009-1: name + size only — file ids stay internal at MVP).</summary>
/// <param name="Name">Original file name.</param>
/// <param name="SizeBytes">File size in bytes.</param>
public sealed record GetMyTransferFile(string Name, long SizeBytes);
