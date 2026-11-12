namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-014-04: Pure template locale selection — no side effects, unit-testable per locale.
/// Resolves the template locale for a recipient based on their stored preference (ADR-011).
/// </summary>
public static class TemplateLocaleResolver
{
    private static readonly string[] SupportedLocales = ["en", "nl", "fr", "es", "pt", "it", "de", "tr"];

    /// <summary>
    /// Resolve the template locale for a recipient.
    /// - If preferredLanguage is null/empty → "en"
    /// - If preferredLanguage is not in SupportedLocales → "en" (fallback)
    /// - Otherwise → preferredLanguage (lowercased, trimmed)
    /// </summary>
    public static string Resolve(string? preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(preferredLanguage))
            return "en";

        var normalized = preferredLanguage.Trim().ToLowerInvariant();
        return SupportedLocales.Contains(normalized) ? normalized : "en";
    }

    /// <summary>
    /// Check if a locale is supported (has both .html and .txt templates).
    /// Used for parity validation.
    /// </summary>
    public static bool IsSupported(string locale) => SupportedLocales.Contains(locale);
}
