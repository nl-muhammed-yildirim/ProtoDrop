using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-05 / T-006-06: Picks the IEmailSender by configuration (TA-0.2 rule 7). Wa:Email:Sender == "log" selects the
/// log-only sender; anything else the Communication Hub sender, which requires Wa:CommunicationHub:Connection
/// (TA-9.3). A missing connection throws (names the key only, TA-9.4) instead of silently falling back to log.
/// Reads through IConfiguration so both "Wa:X" and the Linux "Wa__X" forms work.
/// </summary>
public static class EmailSenderSelector
{
    /// <summary>The sender key constant; value <c>Wa:Email:Sender</c>.</summary>
    public const string SenderKey = "Wa:Email:Sender";
    /// <summary>The from address key constant; value <c>Wa:Email:FromAddress</c>.</summary>
    public const string FromAddressKey = "Wa:Email:FromAddress";
    /// <summary>The connection key constant; value <c>Wa:CommunicationHub:Connection</c>.</summary>
    public const string ConnectionKey = "Wa:CommunicationHub:Connection";
    /// <summary>The default from address constant; value <c>no-reply@protodrop.com</c>.</summary>
    public const string DefaultFromAddress = "no-reply@protodrop.com";

    /// <summary>Creates the i email sender.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <returns>The i email sender.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the operation is not valid in the current state.
    /// </exception>
    public static IEmailSender Create(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        if (configuration[SenderKey] == "log")
            return new LogEmailSender(loggerFactory.CreateLogger<LogEmailSender>());

        var connection = configuration[ConnectionKey];
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                $"{ConnectionKey} is required when {SenderKey} is not 'log' (set {SenderKey}=log for local dev).");

        return new CommunicationHubEmailSender(
            connection,
            configuration[FromAddressKey] ?? DefaultFromAddress,
            loggerFactory.CreateLogger<CommunicationHubEmailSender>());
    }
}
