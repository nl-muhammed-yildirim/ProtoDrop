namespace wa.application.Ports;

/// <summary>A rendered email ready to send (TA-6.7).</summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string From,
    string? ReplyTo);
