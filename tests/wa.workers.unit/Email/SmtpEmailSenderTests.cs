using System.Net.Mime;
using wa.application.Ports;
using wa.infrastructure.Email;
using Xunit;

namespace wa.workers.unit.Email;

/// <summary>CHORE-local-email: shape of the MIME message the SMTP sender hands to Mailpit.</summary>
public class SmtpEmailSenderTests
{
    private static EmailMessage Message(string? replyTo) =>
        new("to@x.test", "Hello wörld", "<p>Hi</p>", "Hi", "ignored@x.test", replyTo);

    [Fact]
    public void CreateMailMessage_UsesConfiguredFromAndRecipient()
    {
        using var mail = SmtpEmailSender.CreateMailMessage(Message(null), "no-reply@protodrop.com");

        Assert.Equal("no-reply@protodrop.com", mail.From?.Address);
        Assert.Equal("to@x.test", Assert.Single(mail.To).Address);
        Assert.Equal("Hello wörld", mail.Subject);
        Assert.Empty(mail.ReplyToList);
    }

    [Fact]
    public void CreateMailMessage_ReplyToPresent_IsAdded()
    {
        using var mail = SmtpEmailSender.CreateMailMessage(Message("sender@x.test"), "no-reply@protodrop.com");

        Assert.Equal("sender@x.test", Assert.Single(mail.ReplyToList).Address);
    }

    [Fact]
    public void CreateMailMessage_HasTextViewThenHtmlView()
    {
        using var mail = SmtpEmailSender.CreateMailMessage(Message(null), "no-reply@protodrop.com");

        Assert.Equal(2, mail.AlternateViews.Count);
        Assert.Equal(MediaTypeNames.Text.Plain, mail.AlternateViews[0].ContentType.MediaType);
        Assert.Equal("Hi", Read(mail.AlternateViews[0]));
        Assert.Equal(MediaTypeNames.Text.Html, mail.AlternateViews[1].ContentType.MediaType);
        Assert.Equal("<p>Hi</p>", Read(mail.AlternateViews[1]));
    }

    private static string Read(System.Net.Mail.AlternateView view)
    {
        using var reader = new StreamReader(view.ContentStream);
        return reader.ReadToEnd();
    }
}
