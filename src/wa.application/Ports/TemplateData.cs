namespace wa.application.Ports;

/// <summary>Data passed to the template renderer (TA-6.7).</summary>
/// <param name="SenderName">The sender name of the template data.</param>
/// <param name="Files">The files of the template data.</param>
/// <param name="CtaUrl">The cta URL of the template data.</param>
/// <param name="ExpiryDays">The expiry days of the template data.</param>
/// <param name="UnsubscribeUrl">The unsubscribe URL of the template data.</param>
public sealed record TemplateData(
    string SenderName,
    IReadOnlyList<FileEntry> Files,
    string CtaUrl,
    int ExpiryDays,
    string? UnsubscribeUrl = null);
