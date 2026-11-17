namespace wa.application.Ports;

/// <summary>A rendered email ready to send (TA-6.7).</summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string From,
    string? ReplyTo);

/// <summary>Email sending port — implemented by CommunicationHubEmailSender (prod) or LogEmailSender (dev).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
