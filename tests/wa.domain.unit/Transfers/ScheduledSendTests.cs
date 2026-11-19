using wa.domain.Transfers;

namespace wa.domain.unit.Transfers;

/// <summary>US-022-04 (FR-022-4/5/7, AC-022-5, EC-022-3): scheduled-send lifecycle predicates.</summary>
public class ScheduledSendTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TransferStatus.Draft, false)]
    [InlineData(TransferStatus.Active, false)]
    [InlineData(TransferStatus.Expired, false)]
    [InlineData(TransferStatus.DownloadLimit, false)]
    [InlineData(TransferStatus.Deleted, false)]
    [InlineData(TransferStatus.Scheduled, true)]
    public void IsDue_OnlyScheduledStatus(byte status, bool expected) =>
        Assert.Equal(expected, ScheduledSend.IsDue(status, Now.AddMinutes(-1), Now));

    [Theory]
    [InlineData(TransferStatus.Draft, false)]
    [InlineData(TransferStatus.Active, false)]
    [InlineData(TransferStatus.Expired, false)]
    [InlineData(TransferStatus.DownloadLimit, false)]
    [InlineData(TransferStatus.Deleted, false)]
    [InlineData(TransferStatus.Scheduled, true)]
    public void CanActivate_OnlyScheduled_NotExpired(byte status, bool expected) =>
        Assert.Equal(expected, ScheduledSend.CanActivate(status, Now.AddMinutes(-1), Now.AddDays(1), Now));

    [Fact]
    public void CanActivate_ScheduledPastExpiry_False_ExpiryWins()
    {
        Assert.False(ScheduledSend.CanActivate(TransferStatus.Scheduled, Now.AddMinutes(-1), Now.AddHours(-1), Now));
        Assert.False(ScheduledSend.CanActivate(TransferStatus.Scheduled, Now.AddMinutes(-1), Now, Now));
    }

    [Fact]
    public void IsDue_Boundary_EqualToNowIsDue_FutureIsNot_NullNever()
    {
        Assert.True(ScheduledSend.IsDue(TransferStatus.Scheduled, Now, Now));
        Assert.False(ScheduledSend.IsDue(TransferStatus.Scheduled, Now.AddTicks(1), Now));
        Assert.False(ScheduledSend.IsDue(TransferStatus.Scheduled, Now.AddHours(2), Now));
        Assert.False(ScheduledSend.IsDue(TransferStatus.Scheduled, null, Now));
    }

    [Theory]
    [InlineData(TransferStatus.Draft)]
    [InlineData(TransferStatus.Active)]
    [InlineData(TransferStatus.Expired)]
    [InlineData(TransferStatus.DownloadLimit)]
    [InlineData(TransferStatus.Deleted)]
    [InlineData(TransferStatus.Scheduled)]
    public void MayNotify_NullSchedule_AlwaysTrue_LegacyPath(byte status) =>
        Assert.True(ScheduledSend.MayNotify(status, null));

    [Theory]
    [InlineData(TransferStatus.Draft, false)]
    [InlineData(TransferStatus.Active, true)]
    [InlineData(TransferStatus.Expired, false)]
    [InlineData(TransferStatus.DownloadLimit, false)]
    [InlineData(TransferStatus.Deleted, false)]
    [InlineData(TransferStatus.Scheduled, false)]
    public void MayNotify_ScheduledRow_OnlyWhenActive(byte status, bool expected) =>
        Assert.Equal(expected, ScheduledSend.MayNotify(status, Now));

    [Fact]
    public void LagSeconds_WholeSeconds_ClampedAtZero()
    {
        Assert.Equal(300, ScheduledSend.LagSeconds(Now, Now.AddMinutes(-5)));
        Assert.Equal(0, ScheduledSend.LagSeconds(Now, Now));
        Assert.Equal(0, ScheduledSend.LagSeconds(Now, Now.AddMinutes(5)));
    }
}
