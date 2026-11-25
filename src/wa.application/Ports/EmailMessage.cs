namespace wa.application.Ports;

/// <summary>A rendered email ready to send (TA-6.7).</summary>
/// <param name="To">The to of the email message.</param>
/// <param name="Subject">The subject of the email message.</param>
/// <param name="HtmlBody">The HTML body of the email message.</param>
/// <param name="TextBody">The text body of the email message.</param>
/// <param name="From">The from of the email message.</param>
/// <param name="ReplyTo">The reply to of the email message.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string From,
    string? ReplyTo);
