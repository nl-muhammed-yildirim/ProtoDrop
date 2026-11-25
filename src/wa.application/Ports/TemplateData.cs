namespace wa.application.Ports;

/// <summary>Data passed to the template renderer (TA-6.7).</summary>
public sealed record TemplateData(
    string SenderName,
    IReadOnlyList<FileEntry> Files,
    string CtaUrl,
    int ExpiryDays,
    string? UnsubscribeUrl = null);
