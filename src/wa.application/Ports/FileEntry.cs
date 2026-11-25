namespace wa.application.Ports;

/// <summary>A file entry for template rendering (name + size).</summary>
/// <param name="Name">File name as shown in the email list.</param>
/// <param name="SizeBytes">File size in bytes; formatted by the template.</param>
public sealed record FileEntry(string Name, long SizeBytes);
