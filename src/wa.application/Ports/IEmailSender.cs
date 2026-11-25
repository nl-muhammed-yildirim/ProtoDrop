namespace wa.application.Ports;

/// <summary>Email sending port — implemented by CommunicationHubEmailSender (prod) or LogEmailSender (dev).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
