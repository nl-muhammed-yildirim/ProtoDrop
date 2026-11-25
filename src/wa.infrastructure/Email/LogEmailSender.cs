using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-01: Log-only email sender (dev switch: Wa:Email:Sender=log).
/// Visible in dev run for T-019 exit check.
/// </summary>
/// <param name="logger">Logger that receives the mail details instead of a real mail provider.</param>
public sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger = logger;

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        LogEmailSent(_logger, message.To, message.From, message.ReplyTo ?? "(none)", message.Subject, message.HtmlBody, message.TextBody);

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "EMAIL SENT\nTo: {To}\nFrom: {From}\nReplyTo: {ReplyTo}\nSubject: {Subject}\n\nHTML:\n{HtmlBody}\n\nText:\n{TextBody}")]
    private static partial void LogEmailSent(ILogger logger, string to, string from, string replyTo, string subject, string htmlBody, string textBody);
}
