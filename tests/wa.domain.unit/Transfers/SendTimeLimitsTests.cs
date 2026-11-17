using wa.domain.Transfers;

namespace wa.domain.unit.Transfers;

/// <summary>T-031-04 (FR-018-4, US-018-04): the send-time stamping rule. Values are test data, not plan constants.</summary>
public class SendTimeLimitsTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    private static Transfer NewTransfer(byte status) => new()
    {
        Id = Guid.NewGuid(),
        LinkId = "abcd1234",
        Status = status,
        SenderName = "Guest",
        CreatedAtUtc = Now
    };

    [Fact]
    public void Stamp_Draft_SetsExpiryFromRetentionDays_AndCapFromMaxDownloads()
    {
        var transfer = NewTransfer(0);

        SendTimeLimits.Stamp(transfer, new LimitsRecord { RetentionDays = 30, MaxDownloads = 1000 }, Now);

        Assert.Equal(Now.AddDays(30), transfer.ExpiresAtUtc);
        Assert.Equal(1000, transfer.MaxDownloads);
        Assert.Equal(0, transfer.Status);
    }

    [Fact]
    public void Stamp_UnlimitedMaxDownloads_CopiesSentinel()
    {
        var transfer = NewTransfer(0);

        SendTimeLimits.Stamp(transfer, new LimitsRecord { RetentionDays = 30, MaxDownloads = -1 }, Now);

        Assert.Equal(-1, transfer.MaxDownloads);
    }

    [Fact]
    public void Stamp_ActiveTransfer_Throws_AndLeavesSendTimeValuesUnchanged()
    {
        var transfer = NewTransfer(1);
        transfer.ExpiresAtUtc = Now.AddDays(30);
        transfer.MaxDownloads = 1000;
        var expiry = transfer.ExpiresAtUtc;

        Assert.Throws<InvalidOperationException>(() =>
            SendTimeLimits.Stamp(transfer, new LimitsRecord { RetentionDays = 7, MaxDownloads = 100 }, Now));

        Assert.Equal(expiry, transfer.ExpiresAtUtc);
        Assert.Equal(1000, transfer.MaxDownloads);
    }
}
