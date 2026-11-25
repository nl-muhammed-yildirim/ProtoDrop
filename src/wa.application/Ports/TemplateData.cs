namespace wa.application.Ports;

/// <summary>Data passed to the template renderer (TA-6.7).</summary>
/// <param name="SenderName">Name of the person who sent the transfer.</param>
/// <param name="Files">Files listed in the email.</param>
/// <param name="CtaUrl">Link behind the call-to-action button (the transfer download page).</param>
/// <param name="ExpiryDays">Number of days until the transfer expires, shown in the body.</param>
/// <param name="UnsubscribeUrl">Link that suppresses further emails to the recipient; null to omit it.</param>
public sealed record TemplateData(
    string SenderName,
    IReadOnlyList<FileEntry> Files,
    string CtaUrl,
    int ExpiryDays,
    string? UnsubscribeUrl = null);
