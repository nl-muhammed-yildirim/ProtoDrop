namespace wa.application.Ports;

/// <summary>Email sending port — implemented by CommunicationHubEmailSender (prod) or LogEmailSender (dev).</summary>
public interface IEmailSender
{
    /// <summary>Hands one rendered email to the mail provider.</summary>
    /// <param name="message">The fully rendered message to deliver.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
