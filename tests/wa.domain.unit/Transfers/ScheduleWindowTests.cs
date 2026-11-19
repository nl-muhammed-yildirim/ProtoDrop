using wa.domain.Errors;
using wa.domain.Transfers;

namespace wa.domain.unit.Transfers;

/// <summary>US-022-03 (FR-022-2, AC-022-2, AC-022-3): the schedule window is now+5 min .. now+14 days, inclusive.</summary>
public class ScheduleWindowTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Min_IsFiveMinutes_Max_IsFourteenDays()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ScheduleWindow.Min);
        Assert.Equal(TimeSpan.FromDays(14), ScheduleWindow.Max);
        Assert.Equal(5, ScheduleWindow.MinMinutes);
        Assert.Equal(14, ScheduleWindow.MaxDays);
    }

    [Fact]
    public void Contains_ExactlyMin_True() => Assert.True(ScheduleWindow.Contains(Now + ScheduleWindow.Min, Now));

    [Fact]
    public void Contains_ExactlyMax_True() => Assert.True(ScheduleWindow.Contains(Now + ScheduleWindow.Max, Now));

    [Fact]
    public void Contains_OneTickBeforeMin_False() => Assert.False(ScheduleWindow.Contains(Now + ScheduleWindow.Min - TimeSpan.FromTicks(1), Now));

    [Fact]
    public void Contains_OneTickAfterMax_False() => Assert.False(ScheduleWindow.Contains(Now + ScheduleWindow.Max + TimeSpan.FromTicks(1), Now));

    [Fact]
    public void Contains_PlusTwoMinutes_False() => Assert.False(ScheduleWindow.Contains(Now.AddMinutes(2), Now));

    [Fact]
    public void Contains_PlusThirtyDays_False() => Assert.False(ScheduleWindow.Contains(Now.AddDays(30), Now));

    [Fact]
    public void Contains_Past_False() => Assert.False(ScheduleWindow.Contains(Now.AddHours(-1), Now));

    [Fact]
    public void OutOfWindowMessage_NamesBothLimits()
    {
        Assert.Contains("5 minutes", ScheduleWindow.OutOfWindowMessage);
        Assert.Contains("14 days", ScheduleWindow.OutOfWindowMessage);
    }

    [Fact]
    public void ScheduleOutOfWindow_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("SCHEDULE_OUT_OF_WINDOW", ProblemCodes.ScheduleOutOfWindow);
        Assert.Equal(422, ProblemCodes.StatusOf(ProblemCodes.ScheduleOutOfWindow));
        Assert.Equal("Schedule out of window", ProblemCodes.TitleOf(ProblemCodes.ScheduleOutOfWindow));
    }
}
