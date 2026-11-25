namespace wa.application.Ports;

/// <summary>Template rendering port — implemented by StubbleTemplateRenderer. T-006-04: per-language rendering with locale parameter.</summary>
public interface ITemplateRenderer
{
    /// <summary>Renders the email template for a locale, falling back to English when the locale has no template.</summary>
    /// <param name="data">Values substituted into the template.</param>
    /// <param name="locale">Language tag of the recipient, for example <c>en</c> or <c>nl</c>.</param>
    /// <returns>The rendered subject, HTML body and plain-text body.</returns>
    (string Subject, string HtmlBody, string TextBody) Render(TemplateData data, string locale);
}
