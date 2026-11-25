namespace wa.domain.Transfers;

/// <summary>
/// US-022-03 (FR-022-2): the scheduled-send window, now + 5 minutes .. now + 14 days, inclusive on both ends.
/// A code constant that applies to every plan (precedent: BillingGrace.Duration), not a Limits Registry entry.
/// The SPA receives these values from GET /billing/plan; no other code carries the numbers.
/// </summary>
public static class ScheduleWindow
{
    /// <summary>Earliest allowed distance of a scheduled send from now: 5 minutes (FR-022-2).</summary>
    public static readonly TimeSpan Min = TimeSpan.FromMinutes(5);
    /// <summary>Latest allowed distance of a scheduled send from now: 14 days (FR-022-2).</summary>
    public static readonly TimeSpan Max = TimeSpan.FromDays(14);

    /// <summary>The minimum lead time in whole minutes, as published to the SPA.</summary>
    public static int MinMinutes => (int)Min.TotalMinutes;
    /// <summary>The maximum lead time in whole days, as published to the SPA.</summary>
    public static int MaxDays => (int)Max.TotalDays;

    /// <summary>Tells whether a requested send time lies inside the window; inclusive on both ends (story: exactly +5 min and +14 days are valid).</summary>
    /// <param name="dueUtc">The requested send time in UTC.</param>
    /// <param name="nowUtc">Current server time in UTC.</param>
    /// <returns><see langword="true"/> when <paramref name="dueUtc"/> is between now + <see cref="Min"/> and now + <see cref="Max"/>; otherwise <see langword="false"/>.</returns>
    public static bool Contains(DateTime dueUtc, DateTime nowUtc) => dueUtc >= nowUtc + Min && dueUtc <= nowUtc + Max;

    /// <summary>The validation message returned with SCHEDULE_OUT_OF_WINDOW, built from the window constants.</summary>
    public static string OutOfWindowMessage =>
        $"scheduledSendAt must be between {MinMinutes} minutes and {MaxDays} days from now (server time).";
}
