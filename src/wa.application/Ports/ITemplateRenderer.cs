namespace wa.application.Ports;

/// <summary>Template rendering port — implemented by StubbleTemplateRenderer. T-006-04: per-language rendering with locale parameter.</summary>
public interface ITemplateRenderer
{
    /// <summary>Renders the value tuple of text value.</summary>
    /// <param name="data">The data.</param>
    /// <param name="locale">The locale.</param>
    /// <returns>A tuple holding the subject and HTML body and text body values.</returns>
    (string Subject, string HtmlBody, string TextBody) Render(TemplateData data, string locale);
}
