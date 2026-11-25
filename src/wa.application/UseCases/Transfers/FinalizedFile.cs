namespace wa.application.UseCases.Transfers;

/// <summary>A file in the finalized transfer result.</summary>
/// <param name="FileId">File id from the create-draft call.</param>
/// <param name="Name">Original file name.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="ContentType">MIME type, or null when unknown.</param>
public sealed record FinalizedFile(Guid FileId, string Name, long SizeBytes, string? ContentType);
