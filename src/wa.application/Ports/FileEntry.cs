namespace wa.application.Ports;

/// <summary>A file entry for template rendering (name + size).</summary>
public sealed record FileEntry(string Name, long SizeBytes);
