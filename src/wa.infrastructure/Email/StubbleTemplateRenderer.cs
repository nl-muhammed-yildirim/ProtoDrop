using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Stubble.Core;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-04: Stubble template renderer — per-language rendering with locale-aware formatting and English fallback.
/// Loads all supported locale templates from Emails/templates/transfer/. Emits email.template_fallback on unknown locale,
/// template_render_error when unreplaced Stubble variables are detected in output.
/// </summary>
public sealed class StubbleTemplateRenderer : ITemplateRenderer
{
    private readonly Dictionary<string, (string Html, string Txt)> _templates;
    private readonly ILogger<StubbleTemplateRenderer> _logger;

    /// <summary>Per-locale subject format strings (TA-6.7: one place, never inline in code).</summary>
    private static readonly Dictionary<string, string> SubjectFormats = new()
    {
        ["en"] = "Transfer from {0}",
        ["nl"] = "Overdracht van {0}",
        ["fr"] = "Transfert de {0}",
        ["es"] = "Transferencia de {0}",
        ["pt"] = "Transferência de {0}",
        ["it"] = "Trasferimento da {0}",
        ["de"] = "Übermittlung von {0}",
        ["tr"] = "{0} tarafından aktarım"
    };

    private static readonly string[] SupportedLocales = ["en", "nl", "fr", "es", "pt", "it", "de", "tr"];

    public StubbleTemplateRenderer(ILogger<StubbleTemplateRenderer> logger)
    {
        _logger = logger;
        var basePath = Path.Combine(AppContext.BaseDirectory, "Emails", "templates", "transfer");
        _templates = new Dictionary<string, (string Html, string Txt)>();

        foreach (var lang in SupportedLocales)
        {
            var htmlPath = Path.Combine(basePath, $"{lang}.html");
            var txtPath = Path.Combine(basePath, $"{lang}.txt");
            if (File.Exists(htmlPath) && File.Exists(txtPath))
                _templates[lang] = (File.ReadAllText(htmlPath), File.ReadAllText(txtPath));
        }
    }

    public (string Subject, string HtmlBody, string TextBody) Render(TemplateData data, string locale)
    {
        // Resolve culture for number formatting.
        CultureInfo? culture = null;
        try { culture = new CultureInfo(locale); } catch { /* invalid locale code */ }

        // Pre-compute human-readable sizes with locale-aware number format.
        var filesWithSizes = data.Files.Select(f => new TemplateFileItem(
            f.Name, FormatBytes(f.SizeBytes, culture))).ToList();

        var templateData = new TemplateRenderModel(
            data.SenderName, filesWithSizes, data.CtaUrl, data.ExpiryDays);

        // Resolve locale with fallback to English.
        if (!_templates.TryGetValue(locale, out var templates))
        {
            _logger.LogInformation("email.template_fallback");
            locale = "en";
            templates = _templates["en"];
        }

        var htmlBody = StaticStubbleRenderer.Render(templates.Html, templateData);
        var textBody = StaticStubbleRenderer.Render(templates.Txt, templateData);

        // Subject from per-locale format string.
        var subjectFormat = SubjectFormats.TryGetValue(locale, out var fmt) ? fmt : SubjectFormats["en"];
        var subject = string.Format(culture ?? CultureInfo.InvariantCulture, subjectFormat, data.SenderName);

        // Detect unreplaced Stubble variables ({{...}} patterns in output).
        if (Regex.IsMatch(htmlBody + textBody, @"\{\{[^}]+\}\}") || Regex.IsMatch(subject, @"\{\{"))
        {
            _logger.LogInformation("template_render_error");
        }

        return (subject, htmlBody, textBody);
    }

    /// <summary>Format bytes using 1024-based units with locale-aware number formatting.</summary>
    private static string FormatBytes(long bytes, CultureInfo? culture)
    {
        if (bytes < 1024) return $"{bytes} B";

        var units = new[] { "KB", "MB", "GB" };
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        // Locale-aware number format: de → "1,5", en → "1.5"
        var formatted = value.ToString("F1", culture ?? CultureInfo.InvariantCulture);
        return $"{formatted} {units[unitIndex]}";
    }

    /// <summary>Stubble-compatible file item with explicit string properties.</summary>
    private sealed class TemplateFileItem(string Name, string Size)
    {
        public string Name { get; init; } = Name;
        public string Size { get; init; } = Size;
    }

    /// <summary>Stubble-compatible render model with explicit properties.</summary>
    private sealed class TemplateRenderModel(
        string SenderName,
        IReadOnlyList<TemplateFileItem> Files,
        string CtaUrl,
        int ExpiryDays)
    {
        public string SenderName { get; init; } = SenderName;
        public IReadOnlyList<TemplateFileItem> Files { get; init; } = Files;
        public string CtaUrl { get; init; } = CtaUrl;
        public int ExpiryDays { get; init; } = ExpiryDays;
    }
}
