using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.infrastructure.Email;

/// <summary>
/// T-006-05 / T-006-06: Picks the IEmailSender by configuration (TA-0.2 rule 7). Wa:Email:Sender == "log" selects the
/// log-only sender; "smtp" (CHORE-local-email) the plain-SMTP sender for a local catcher such as Mailpit; anything else
/// the Communication Hub sender, which requires Wa:CommunicationHub:Connection (TA-9.3). A missing connection throws
/// (names the key only, TA-9.4) instead of silently falling back to log. Both log and smtp are refused outside Local/Development.
/// Reads through IConfiguration so both "Wa:X" and the Linux "Wa__X" forms work.
/// </summary>
public static class EmailSenderSelector
{
    /// <summary>Configuration key that selects the sender; <c>log</c> chooses the log-only sender, <c>smtp</c> the SMTP sender.</summary>
    public const string SenderKey = "Wa:Email:Sender";
    /// <summary>Configuration key for the From address of outgoing mail.</summary>
    public const string FromAddressKey = "Wa:Email:FromAddress";
    /// <summary>Configuration key for the Communication Services connection string (a Key Vault secret in production).</summary>
    public const string ConnectionKey = "Wa:CommunicationHub:Connection";
    /// <summary>Configuration key for the SMTP host used by the <c>smtp</c> sender.</summary>
    public const string SmtpHostKey = "Wa:Email:Smtp:Host";
    /// <summary>Configuration key for the SMTP port used by the <c>smtp</c> sender.</summary>
    public const string SmtpPortKey = "Wa:Email:Smtp:Port";
    /// <summary>From address used when <see cref="FromAddressKey"/> is not configured.</summary>
    public const string DefaultFromAddress = "no-reply@protodrop.com";

    /// <summary>Value of <see cref="SenderKey"/> that selects the log-only sender.</summary>
    public const string LogSender = "log";
    /// <summary>Value of <see cref="SenderKey"/> that selects the plain-SMTP sender (local Mailpit).</summary>
    public const string SmtpSender = "smtp";

    /// <summary>
    /// Configuration keys that name the host environment, first non-blank wins. Read from configuration (not
    /// <c>IHostEnvironment</c>) because the isolated worker host does not reliably map AZURE_FUNCTIONS_ENVIRONMENT.
    /// </summary>
    public static readonly IReadOnlyList<string> EnvironmentKeys =
        ["AZURE_FUNCTIONS_ENVIRONMENT", "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"];

    /// <summary>Tells whether the host environment is Local or Development. An unset environment counts as Production.</summary>
    /// <param name="configuration">Configuration source; reads the environment keys.</param>
    /// <returns><see langword="true"/> when the first non-blank environment key is Local or Development (case-insensitive).</returns>
    public static bool IsLocalOrDevelopment(IConfiguration configuration)
    {
        var environment = EnvironmentKeys.Select(k => configuration[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CHORE-deploy-hardening (TA-9.4): refuses the log-only sender (it writes message bodies, so magic links, to the logs) and the
    /// SMTP sender (CHORE-local-email: plain SMTP, no TLS) outside Local/Development. An unset environment counts as Production.
    /// </summary>
    /// <param name="configuration">Configuration source; reads the sender key and the environment keys.</param>
    /// <exception cref="InvalidOperationException">Thrown when the sender is <c>log</c> or <c>smtp</c> and the environment is not Local or Development (names keys, the chosen sender and the environment, never a secret).</exception>
    public static void EnsureSenderAllowed(IConfiguration configuration)
    {
        var sender = configuration[SenderKey];
        if (!string.Equals(sender, LogSender, StringComparison.Ordinal)
            && !string.Equals(sender, SmtpSender, StringComparison.Ordinal))
            return;

        if (IsLocalOrDevelopment(configuration))
            return;

        var environment = EnvironmentKeys.Select(k => configuration[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        throw new InvalidOperationException(
            $"{SenderKey}={sender} is allowed only when the host environment is Local or Development "
            + $"(AZURE_FUNCTIONS_ENVIRONMENT / DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT); current: '{environment ?? "(unset = Production)"}'.");
    }

    /// <summary>Builds the email sender that the configuration selects.</summary>
    /// <param name="configuration">Configuration source; reads the sender, from-address, SMTP and connection keys.</param>
    /// <param name="loggerFactory">Factory for the logger of the chosen sender.</param>
    /// <returns>The log-only sender when the sender key is <c>log</c>; the SMTP sender when it is <c>smtp</c>; otherwise a Communication Services sender.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the log or smtp sender is selected outside Local/Development, when smtp lacks a valid host or port, or when a real sender is selected but the connection string is missing (messages name keys only).</exception>
    public static IEmailSender Create(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        EnsureSenderAllowed(configuration);
        if (configuration[SenderKey] == LogSender)
            return new LogEmailSender(loggerFactory.CreateLogger<LogEmailSender>());

        if (configuration[SenderKey] == SmtpSender)
        {
            var host = configuration[SmtpHostKey];
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException($"{SmtpHostKey} is required when {SenderKey}={SmtpSender}.");
            if (!int.TryParse(configuration[SmtpPortKey], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
                || port is < 1 or > 65535)
                throw new InvalidOperationException($"{SmtpPortKey} must be a port number between 1 and 65535 when {SenderKey}={SmtpSender}.");

            return new SmtpEmailSender(host, port, configuration[FromAddressKey] ?? DefaultFromAddress);
        }

        var connection = configuration[ConnectionKey];
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                $"{ConnectionKey} is required when {SenderKey} is not 'log' or 'smtp' (set {SenderKey}=log for local dev).");

        return new CommunicationHubEmailSender(
            connection,
            configuration[FromAddressKey] ?? DefaultFromAddress,
            loggerFactory.CreateLogger<CommunicationHubEmailSender>());
    }
}
