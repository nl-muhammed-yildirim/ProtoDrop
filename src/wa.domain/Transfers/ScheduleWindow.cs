namespace wa.domain.Transfers;

/// <summary>
/// US-022-03 (FR-022-2): the scheduled-send window, now + 5 minutes .. now + 14 days, inclusive on both ends.
/// A code constant that applies to every plan (precedent: BillingGrace.Duration), not a Limits Registry entry.
/// The SPA receives these values from GET /billing/plan; no other code carries the numbers.
/// </summary>
public static class ScheduleWindow
{
    public static readonly TimeSpan Min = TimeSpan.FromMinutes(5);   // FR-022-2
    public static readonly TimeSpan Max = TimeSpan.FromDays(14);     // FR-022-2

    public static int MinMinutes => (int)Min.TotalMinutes;
    public static int MaxDays => (int)Max.TotalDays;

    /// <summary>Inclusive on both ends (story: exactly +5 min and +14 days are valid).</summary>
    public static bool Contains(DateTime dueUtc, DateTime nowUtc) => dueUtc >= nowUtc + Min && dueUtc <= nowUtc + Max;

    public static string OutOfWindowMessage =>
        $"scheduledSendAt must be between {MinMinutes} minutes and {MaxDays} days from now (server time).";
}
