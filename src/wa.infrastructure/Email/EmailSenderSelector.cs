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

    /// <summary>Value of <see cref="SenderKey"/> that selects the log-only sender.</summary>
    public const string LogSender = "log";

    /// <summary>
    /// Configuration keys that name the host environment, first non-blank wins. Read from configuration (not
    /// <c>IHostEnvironment</c>) because the isolated worker host does not reliably map AZURE_FUNCTIONS_ENVIRONMENT.
    /// </summary>
    public static readonly IReadOnlyList<string> EnvironmentKeys =
        ["AZURE_FUNCTIONS_ENVIRONMENT", "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"];

    /// <summary>
    /// CHORE-deploy-hardening (TA-9.4): refuses the log-only sender outside Local/Development, because it writes
    /// message bodies (magic links) to the logs. An unset environment counts as Production.
    /// </summary>
    /// <param name="configuration">Configuration source; reads the sender key and the environment keys.</param>
    /// <exception cref="InvalidOperationException">Thrown when the sender is <c>log</c> and the environment is not Local or Development (names keys and environment, never a secret).</exception>
    public static void EnsureSenderAllowed(IConfiguration configuration)
    {
        if (!string.Equals(configuration[SenderKey], LogSender, StringComparison.Ordinal))
            return;

        var environment = EnvironmentKeys.Select(k => configuration[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Local", StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException(
            $"{SenderKey}={LogSender} is allowed only when the host environment is Local or Development "
            + $"(AZURE_FUNCTIONS_ENVIRONMENT / DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT); current: '{environment ?? "(unset = Production)"}'.");
    }

    /// <summary>Builds the email sender that the configuration selects.</summary>
    /// <param name="configuration">Configuration source; reads the sender, from-address and connection keys.</param>
    /// <param name="loggerFactory">Factory for the logger of the chosen sender.</param>
    /// <returns>The log-only sender when the sender key is <c>log</c>; otherwise a Communication Services sender.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the log sender is selected outside Local/Development, or when a real sender is selected but the connection string is missing (messages name keys only).</exception>
    public static IEmailSender Create(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        EnsureSenderAllowed(configuration);
        if (configuration[SenderKey] == LogSender)
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
