using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using wa.application.Ports;
using wa.infrastructure.Email;

namespace wa.workers.unit.Email;

/// <summary>T-006-04: StubbleTemplateRenderer per-language rendering, locale-aware formatting, fallback + telemetry.</summary>
public class StubbleTemplateRendererTests : IDisposable
{
    private readonly StubbleTemplateRenderer _renderer;
    private readonly CapturingLogger _logger = new();

    public StubbleTemplateRendererTests()
    {
        // The renderer loads templates from AppContext.BaseDirectory/Emails/templates/transfer/.
        // In the test project, the wa.workers csproj Content Include copies them to output.
        _renderer = new StubbleTemplateRenderer(_logger);
    }

    public void Dispose() => (_logger as IDisposable)?.Dispose();

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("it")]
    [InlineData("de")]
    [InlineData("tr")]
    public void Render_EachShippedLocale_NoVisibleStubbleVariables(string locale)
    {
        var data = CreateTestData();

        var (subject, htmlBody, textBody) = _renderer.Render(data, locale);

        // No unreplaced Stubble variables in any output.
        Assert.False(subject.Contains("{{"), $"Subject for {locale} contains unreplaced variable: {subject}");
        Assert.False(htmlBody.Contains("{{"), $"HTML body for {locale} contains unreplaced variable");
        Assert.False(textBody.Contains("{{"), $"Text body for {locale} contains unreplaced variable");

        // File names are present (untranslated proper nouns).
        Assert.Contains("report.pdf", htmlBody);
        Assert.Contains("photo.jpg", textBody);

        // CTA URL is present.
        Assert.Contains("https://protodrop.com/t/ABCD1234", htmlBody);
    }

    [Fact]
    public void Render_GermanLocale_UsesCommaDecimalForFileSize()
    {
        var data = CreateTestData(); // includes 1536 bytes → "1,5 KB" in de (comma decimal)

        var (_, _, textBody) = _renderer.Render(data, "de");

        // German number format: comma as decimal separator.
        Assert.True(textBody.Contains("report.pdf"), "Should contain file name");
        
        // Find the size value after report.pdf
        var idx = textBody.IndexOf("report.pdf");
        var snippet = textBody.Substring(idx, Math.Min(30, textBody.Length - idx));
        Assert.True(snippet.Contains("1,5"), $"Expected '1,5' (German comma decimal) in '{snippet}'");
    }

    [Fact]
    public void Render_EnglishLocale_UsesDotDecimalForFileSize()
    {
        var data = CreateTestData();

        var (_, _, textBody) = _renderer.Render(data, "en");

        // English number format: dot as decimal separator. 1536 bytes = 1.5 KB.
        Assert.True(textBody.Contains("report.pdf"), "Should contain file name");
        
        var idx = textBody.IndexOf("report.pdf");
        var snippet = textBody.Substring(idx, Math.Min(30, textBody.Length - idx));
        Assert.True(snippet.Contains("1.5"), $"Expected '1.5' (English dot decimal) in '{snippet}'");
    }

    [Fact]
    public void Render_UnknownLocale_FallsBackToEnglish_EmitsFallbackTelemetry()
    {
        var data = CreateTestData();

        var (subject, htmlBody, textBody) = _renderer.Render(data, "jp"); // Japanese — not in supported set

        // Fallback telemetry emitted.
        Assert.Contains("email.template_fallback", _logger.Messages);

        // Output is English template content.
        Assert.Contains("has shared files with you", htmlBody);
        Assert.Equal("Transfer from Alice", subject);
    }

    [Fact]
    public void Render_MissingPreferredLanguage_HandledByCallerWithEn()
    {
        // The caller (SendNotificationEmailsHandler) resolves null → "en".
        // Here we just verify that "en" works as the default.
        var data = CreateTestData();

        var (subject, _, _) = _renderer.Render(data, "en");

        Assert.Equal("Transfer from Alice", subject);
    }

    [Fact]
    public void Render_GermanSubject_UsesGermanFormat()
    {
        var data = CreateTestData();

        var (subject, _, _) = _renderer.Render(data, "de");

        Assert.Equal("Übermittlung von Alice", subject);
    }

    [Fact]
    public void Render_DutchSubject_UsesDutchFormat()
    {
        var data = CreateTestData();

        var (subject, _, _) = _renderer.Render(data, "nl");

        Assert.Equal("Overdracht van Alice", subject);
    }

    private static TemplateData CreateTestData() => new(
        SenderName: "Alice",
        Files: [new FileEntry("report.pdf", 1536), new FileEntry("photo.jpg", 1048576)],
        CtaUrl: "https://protodrop.com/t/ABCD1234",
        ExpiryDays: 7);

    /// <summary>Captures log messages for assertion.</summary>
    private sealed class CapturingLogger : ILogger<StubbleTemplateRenderer>, IDisposable
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        public void Dispose() { }
    }
}
