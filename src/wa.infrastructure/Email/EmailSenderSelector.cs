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
    /// <summary>Configuration key that selects the sender; the value <c>log</c> chooses the log-only sender.</summary>
    public const string SenderKey = "Wa:Email:Sender";
    /// <summary>Configuration key for the From address of outgoing mail.</summary>
    public const string FromAddressKey = "Wa:Email:FromAddress";
    /// <summary>Configuration key for the Communication Services connection string (a Key Vault secret in production).</summary>
    public const string ConnectionKey = "Wa:CommunicationHub:Connection";
    /// <summary>From address used when <see cref="FromAddressKey"/> is not configured.</summary>
    public const string DefaultFromAddress = "no-reply@protodrop.com";

    /// <summary>Builds the email sender that the configuration selects.</summary>
    /// <param name="configuration">Configuration source; reads the sender, from-address and connection keys.</param>
    /// <param name="loggerFactory">Factory for the logger of the chosen sender.</param>
    /// <returns>The log-only sender when the sender key is <c>log</c>; otherwise a Communication Services sender.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a real sender is selected but the connection string is missing (the message names the key only).</exception>
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
