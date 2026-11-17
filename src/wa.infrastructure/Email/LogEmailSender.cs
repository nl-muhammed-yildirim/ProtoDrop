using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-01: Log-only email sender (dev switch: Wa:Email:Sender=log).
/// Visible in dev run for T-019 exit check.
/// </summary>
public sealed class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;

    public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "EMAIL SENT\nTo: {To}\nFrom: {From}\nReplyTo: {ReplyTo}\nSubject: {Subject}\n\nHTML:\n{HtmlBody}\n\nText:\n{TextBody}",
            message.To,
            message.From,
            message.ReplyTo ?? "(none)",
            message.Subject,
            message.HtmlBody,
            message.TextBody);

        return Task.CompletedTask;
    }
}
