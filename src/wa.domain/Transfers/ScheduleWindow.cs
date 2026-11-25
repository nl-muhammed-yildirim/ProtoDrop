namespace wa.domain.Transfers;

/// <summary>
/// US-022-03 (FR-022-2): the scheduled-send window, now + 5 minutes .. now + 14 days, inclusive on both ends.
/// A code constant that applies to every plan (precedent: BillingGrace.Duration), not a Limits Registry entry.
/// The SPA receives these values from GET /billing/plan; no other code carries the numbers.
/// </summary>
public static class ScheduleWindow
{
    /// <summary>FR-022-2.</summary>
    public static readonly TimeSpan Min = TimeSpan.FromMinutes(5);
    /// <summary>FR-022-2.</summary>
    public static readonly TimeSpan Max = TimeSpan.FromDays(14);

    /// <summary>Gets the min minutes of the schedule window.</summary>
    public static int MinMinutes => (int)Min.TotalMinutes;
    /// <summary>Gets the max days of the schedule window.</summary>
    public static int MaxDays => (int)Max.TotalDays;

    /// <summary>Inclusive on both ends (story: exactly +5 min and +14 days are valid).</summary>
    /// <param name="dueUtc">The due UTC.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <returns><see langword="true"/> when the operation succeeded; otherwise <see langword="false"/>.</returns>
    public static bool Contains(DateTime dueUtc, DateTime nowUtc) => dueUtc >= nowUtc + Min && dueUtc <= nowUtc + Max;

    /// <summary>Gets the out of window message of the schedule window.</summary>
    public static string OutOfWindowMessage =>
        $"scheduledSendAt must be between {MinMinutes} minutes and {MaxDays} days from now (server time).";
}
