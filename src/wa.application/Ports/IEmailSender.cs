namespace wa.application.Ports;

/// <summary>Email sending port — implemented by CommunicationHubEmailSender (prod) or LogEmailSender (dev).</summary>
public interface IEmailSender
{
    /// <summary>Sends the operation.</summary>
    /// <param name="message">The message.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
