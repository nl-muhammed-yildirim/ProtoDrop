using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-01: Azure Communication Services email sender (production).
/// </summary>
public sealed class CommunicationHubEmailSender : IEmailSender
{
    private readonly Azure.Communication.Email.EmailClient _emailClient;
    private readonly string _fromAddress;
    private readonly ILogger<CommunicationHubEmailSender> _logger;

    public CommunicationHubEmailSender(string fromAddress, ILogger<CommunicationHubEmailSender> logger)
    {
        _emailClient = new Azure.Communication.Email.EmailClient(fromAddress);
        _fromAddress = fromAddress;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var content = new Azure.Communication.Email.EmailContent
        {
            Subject = message.Subject,
            HtmlContent = message.HtmlBody,
            TextContent = message.TextBody
        };

        var emailMsg = new Azure.Communication.Email.EmailMessage(
            _fromAddress,
            message.To,
            content);

        if (!string.IsNullOrEmpty(message.ReplyTo))
            emailMsg.ReplyTo = [message.ReplyTo];

        await _emailClient.SendAsync(emailMsg, ct);
    }
}
