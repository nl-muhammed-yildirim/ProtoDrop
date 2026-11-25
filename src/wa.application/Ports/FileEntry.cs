namespace wa.application.Ports;

/// <summary>A file entry for template rendering (name + size).</summary>
/// <param name="Name">The name of the file entry.</param>
/// <param name="SizeBytes">The size bytes of the file entry.</param>
public sealed record FileEntry(string Name, long SizeBytes);
