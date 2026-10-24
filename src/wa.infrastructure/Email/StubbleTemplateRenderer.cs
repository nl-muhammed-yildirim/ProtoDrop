using Stubble.Core;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-01: Stubble template renderer — loads en.html + en.txt from Emails/templates/transfer/.
/// </summary>
public sealed class StubbleTemplateRenderer : ITemplateRenderer
{
    private readonly string _htmlTemplate;
    private readonly string _textTemplate;

    public StubbleTemplateRenderer()
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "Emails", "templates", "transfer");
        _htmlTemplate = File.ReadAllText(Path.Combine(basePath, "en.html"));
        _textTemplate = File.ReadAllText(Path.Combine(basePath, "en.txt"));
    }

    public (string Subject, string HtmlBody, string TextBody) Render(TemplateData data)
    {
        // Pre-compute human-readable sizes.
        var filesWithSizes = data.Files.Select(f => new
        {
            Name = f.Name,
            Size = FormatBytes(f.SizeBytes)
        }).ToList();

        var templateData = new
        {
            SenderName = data.SenderName,
            Files = filesWithSizes,
            CtaUrl = data.CtaUrl,
            ExpiryDays = data.ExpiryDays
        };

        var htmlBody = Stubble.RenderTemplate(_htmlTemplate, templateData);
        var textBody = Stubble.RenderTemplate(_textTemplate, templateData);
        var subject = $"Transfer from {data.SenderName}";

        return (subject, htmlBody, textBody);
    }

    /// <summary>Format bytes using 1024-based units: B, KB, MB, GB.</summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        var units = new[] { "KB", "MB", "GB" };
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:F1} {units[unitIndex]}";
    }
}
