namespace wa.application.Ports;

/// <summary>A file entry for template rendering (name + size).</summary>
public sealed record FileEntry(string Name, long SizeBytes);

/// <summary>Data passed to the template renderer (TA-6.7).</summary>
public sealed record TemplateData(
    string SenderName,
    IReadOnlyList<FileEntry> Files,
    string CtaUrl,
    int ExpiryDays);

/// <summary>Template rendering port — implemented by StubbleTemplateRenderer. T-006-04: per-language rendering with locale parameter.</summary>
public interface ITemplateRenderer
{
    (string Subject, string HtmlBody, string TextBody) Render(TemplateData data, string locale);
}
