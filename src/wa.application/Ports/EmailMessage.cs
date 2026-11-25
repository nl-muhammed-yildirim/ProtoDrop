namespace wa.application.Ports;

/// <summary>A rendered email ready to send (TA-6.7).</summary>
/// <param name="To">Recipient address; PII (TA-9.4).</param>
/// <param name="Subject">Rendered subject line.</param>
/// <param name="HtmlBody">Rendered HTML part.</param>
/// <param name="TextBody">Rendered plain-text alternative.</param>
/// <param name="From">Sender address shown to the recipient.</param>
/// <param name="ReplyTo">Optional reply-to address (for example the transfer sender); null to omit.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string From,
    string? ReplyTo);
