namespace wa.domain.Transfers;

/// <summary>
/// T-031-04 (FR-018-4, AC-018-3, US-018-04): the single named rule for "plan and limit changes apply at the next finalize".
/// "Send-time limits" are the values ILimitsProvider resolves at the moment of send, not at page render. They are stamped
/// onto the Draft exactly once (retention -> ExpiresAtUtc, download cap -> MaxDownloads); live transfers never re-resolve.
/// Plan switches and limit-flag edits follow the same rule because both only change what the next Resolve returns.
/// </summary>
public static class SendTimeLimits
{
    /// <summary>Stamps expiry and download cap onto a Draft. The -1 unlimited sentinel is copied as is. Does not change Status.</summary>
    /// <param name="transfer">The Draft transfer to stamp.</param>
    /// <param name="limits">Limits resolved for the sender at the moment of send.</param>
    /// <param name="nowUtc">Current UTC time; the base of the new expiry.</param>
    /// <exception cref="InvalidOperationException">The transfer is not a Draft; nothing is written.</exception>
    public static void Stamp(Transfer transfer, LimitsRecord limits, DateTime nowUtc)
    {
        if (transfer.Status != 0)
            throw new InvalidOperationException(
                $"Transfer {transfer.Id} is not a draft (status={transfer.Status}): send-time limits are stamped once; live transfers never re-resolve (FR-018-4).");

        transfer.ExpiresAtUtc = nowUtc.AddDays(limits.RetentionDays);
        transfer.MaxDownloads = limits.MaxDownloads;
    }
}
