using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.domain.Privacy;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-01: Log-only email sender (dev switch: Wa:Email:Sender=log).
/// Visible in dev run for T-019 exit check. TA-9.4: recipient and reply-to addresses are masked (<see cref="EmailMask"/>);
/// bodies stay (dev needs the magic links) and <see cref="EmailSenderSelector.EnsureSenderAllowed"/> confines this sender to Local/Development.
/// </summary>
/// <param name="logger">Logger that receives the mail details instead of a real mail provider.</param>
public sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger = logger;

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var to = EmailMask.Mask(message.To) ?? "(none)";
        var replyTo = EmailMask.Mask(message.ReplyTo) ?? "(none)";
        LogEmailSent(_logger, to, message.From, replyTo, message.Subject, message.HtmlBody, message.TextBody);

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "EMAIL SENT\nTo: {To}\nFrom: {From}\nReplyTo: {ReplyTo}\nSubject: {Subject}\n\nHTML:\n{HtmlBody}\n\nText:\n{TextBody}")]
    private static partial void LogEmailSent(ILogger logger, string to, string from, string replyTo, string subject, string htmlBody, string textBody);
}
