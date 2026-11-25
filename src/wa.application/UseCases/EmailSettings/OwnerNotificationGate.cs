using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.domain;

namespace wa.application.UseCases.EmailSettings;

/// <summary>
/// F-XCT-003 (FR-046-3/5/6, EC-046-1): f-email's send-time check for OWNER notification emails only — recipient, auth and
/// billing emails never call it (FR-046-4). No publisher or telemetry dependency: a skipped send emits nothing (TA-10.2 closed list).
/// </summary>
/// <param name="store">Store that holds the owner's notification choice.</param>
/// <param name="flags">Resolver for the email-settings feature gate.</param>
/// <param name="logger">Logger for the skipped-send message.</param>
public sealed partial class OwnerNotificationGate(IEmailNotificationsStore store, IFlagResolver flags, ILogger<OwnerNotificationGate> logger)
{
    private readonly IEmailNotificationsStore _store = store;
    private readonly IFlagResolver _flags = flags;
    private readonly ILogger<OwnerNotificationGate> _logger = logger;

    /// <summary>True when the owner notification may be sent. Reads the column on every call (send time, no cache).</summary>
    /// <param name="ownerAppUserId">The transfer owner; null for anonymous transfers.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the owner should be notified (anonymous owner, gate off, or setting on); <see langword="false"/> when the owner opted out.</returns>
    public async Task<bool> MaySendAsync(Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is not { } ownerId)
            return true; // FR-046-5: guest transfers have no setting
        if (!await _flags.ResolveFlag(FeatureGates.EmailSettings, FeatureGates.DefaultWhenAbsent, ct))
            return true; // FR-046-6 / EC-046-4: flag off = column inert
        if (AppUser.EmailNotificationsOn(await _store.GetAsync(ownerId, ct)))
            return true;

        LogNotificationSkipped(_logger, ownerId);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "f-email: owner notification skipped for {OwnerAppUserId}, suppressedReason=email_setting")]
    private static partial void LogNotificationSkipped(ILogger logger, Guid ownerAppUserId);
}
