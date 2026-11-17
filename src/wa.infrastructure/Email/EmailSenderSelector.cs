using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-05: Picks the IEmailSender by configuration (TA-0.2 rule 7): "log" selects the log-only sender,
/// anything else the Communication Hub sender. Single place for the Wa:Email:Sender switch.
/// </summary>
public static class EmailSenderSelector
{
    public static IEmailSender Create(string? senderSwitch, string fromAddress, ILoggerFactory loggerFactory)
    {
        if (senderSwitch == "log")
            return new LogEmailSender(loggerFactory.CreateLogger<LogEmailSender>());

        return new CommunicationHubEmailSender(
            fromAddress,
            loggerFactory.CreateLogger<CommunicationHubEmailSender>());
    }
}
