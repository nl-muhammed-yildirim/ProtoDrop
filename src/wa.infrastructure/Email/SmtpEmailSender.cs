using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// CHORE-local-email: <see cref="IEmailSender"/> that sends plain SMTP (no TLS, no credentials) to a local catcher such as Mailpit.
/// Selected only in Local/Development (<see cref="EmailSenderSelector"/>). It does not log, so it adds no PII surface (TA-9.4).
/// </summary>
/// <param name="host">SMTP host name (Wa:Email:Smtp:Host).</param>
/// <param name="port">SMTP port (Wa:Email:Smtp:Port).</param>
/// <param name="fromAddress">Sender address of each message (Wa:Email:FromAddress).</param>
public sealed class SmtpEmailSender(string host, int port, string fromAddress) : IEmailSender
{
    private readonly string _host = host;
    private readonly int _port = port;
    private readonly string _fromAddress = fromAddress;

    /// <summary>Builds the MIME message for an <see cref="EmailMessage"/>: plain-text view first, HTML view last (the preferred one).</summary>
    /// <param name="message">The message to send.</param>
    /// <param name="fromAddress">The configured From address.</param>
    /// <returns>A disposable mail message the caller must dispose.</returns>
    public static MailMessage CreateMailMessage(EmailMessage message, string fromAddress)
    {
        var mail = new MailMessage(fromAddress, message.To)
        {
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrEmpty(message.ReplyTo))
            mail.ReplyToList.Add(message.ReplyTo);

        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, Encoding.UTF8, MediaTypeNames.Text.Plain));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        return mail;
    }

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        using var mail = CreateMailMessage(message, _fromAddress);
        using var client = new SmtpClient(_host, _port);
        await client.SendMailAsync(mail, ct);
    }
}
