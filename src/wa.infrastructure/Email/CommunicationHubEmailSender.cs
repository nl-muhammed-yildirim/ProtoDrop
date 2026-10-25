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
        var content = new Azure.Communication.Email.EmailContent(message.Subject)
        {
            Html = message.HtmlBody,
            PlainText = message.TextBody
        };

        var recipients = new Azure.Communication.Email.EmailRecipients(
            new[] { new Azure.Communication.Email.EmailAddress(message.To) });

        var emailMsg = new Azure.Communication.Email.EmailMessage(
            _fromAddress,
            recipients,
            content);

        if (!string.IsNullOrEmpty(message.ReplyTo))
            emailMsg.ReplyTo.Add(new Azure.Communication.Email.EmailAddress(message.ReplyTo));

        await _emailClient.SendAsync(Azure.WaitUntil.Completed, emailMsg, ct);
    }
}
