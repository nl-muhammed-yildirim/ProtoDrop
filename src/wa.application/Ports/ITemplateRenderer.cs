namespace wa.application.Ports;

/// <summary>Template rendering port — implemented by StubbleTemplateRenderer. T-006-04: per-language rendering with locale parameter.</summary>
public interface ITemplateRenderer
{
    (string Subject, string HtmlBody, string TextBody) Render(TemplateData data, string locale);
}
