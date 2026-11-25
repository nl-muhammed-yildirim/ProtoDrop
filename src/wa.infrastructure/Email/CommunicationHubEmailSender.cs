using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>T-006-01: <see cref="IEmailSender"/> that sends through Azure Communication Services Email (production).</summary>
public sealed class CommunicationHubEmailSender : IEmailSender
{
    private readonly Azure.Communication.Email.EmailClient _emailClient;
    private readonly string _fromAddress;
    private readonly ILogger<CommunicationHubEmailSender> _logger;

    /// <summary>Initializes a new instance of the <see cref="CommunicationHubEmailSender"/> class.</summary>
    /// <param name="connectionString">Wa:CommunicationHub:Connection (TA-9.3, Key Vault secret).</param>
    /// <param name="fromAddress">Sender address of each message (Wa:Email:FromAddress).</param>
    /// <param name="logger">Logger for send failures (never logs the message body or address).</param>
    public CommunicationHubEmailSender(string connectionString, string fromAddress, ILogger<CommunicationHubEmailSender> logger)
    {
        _emailClient = new Azure.Communication.Email.EmailClient(connectionString);
        _fromAddress = fromAddress;
        _logger = logger;
    }

    /// <inheritdoc />
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
