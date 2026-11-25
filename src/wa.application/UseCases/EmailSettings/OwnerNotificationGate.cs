using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.domain;

namespace wa.application.UseCases.EmailSettings;

/// <summary>
/// F-XCT-003 (FR-046-3/5/6, EC-046-1): f-email's send-time check for OWNER notification emails only — recipient, auth and
/// billing emails never call it (FR-046-4). No publisher or telemetry dependency: a skipped send emits nothing (TA-10.2 closed list).
/// </summary>
public sealed class OwnerNotificationGate
{
    private readonly IEmailNotificationsStore _store;
    private readonly IFlagResolver _flags;
    private readonly ILogger<OwnerNotificationGate> _logger;

    /// <summary>Initializes a new instance of the <see cref="OwnerNotificationGate"/> class.</summary>
    /// <param name="store">The store.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public OwnerNotificationGate(IEmailNotificationsStore store, IFlagResolver flags, ILogger<OwnerNotificationGate> logger)
    {
        _store = store;
        _flags = flags;
        _logger = logger;
    }

    /// <summary>True when the owner notification may be sent. Reads the column on every call (send time, no cache).</summary>
    /// <param name="ownerAppUserId">Identifier of the owner app user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the operation succeeded; otherwise <see langword="false"/>.</returns>
    public async Task<bool> MaySendAsync(Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is not { } ownerId)
            return true; // FR-046-5: guest transfers have no setting
        if (!await _flags.ResolveFlag(FeatureGates.EmailSettings, FeatureGates.DefaultWhenAbsent, ct))
            return true; // FR-046-6 / EC-046-4: flag off = column inert
        if (AppUser.EmailNotificationsOn(await _store.GetAsync(ownerId, ct)))
            return true;

        _logger.LogInformation("f-email: owner notification skipped for {OwnerAppUserId}, suppressedReason=email_setting", ownerId);
        return false;
    }
}
