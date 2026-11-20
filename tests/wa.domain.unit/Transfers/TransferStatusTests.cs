namespace wa.domain.unit.Transfers;

/// <summary>US-022-01 (D-1): the Status byte values are persisted (TINYINT) — pin them so a renumbering fails loudly.</summary>
public class TransferStatusTests
{
    [Fact]
    public void Values_ArePinned()
    {
        Assert.Equal(0, TransferStatus.Draft);
        Assert.Equal(1, TransferStatus.Active);
        Assert.Equal(2, TransferStatus.Expired);
        Assert.Equal(3, TransferStatus.DownloadLimit);
        Assert.Equal(4, TransferStatus.Deleted);
        Assert.Equal(5, TransferStatus.Scheduled);
    }

    [Fact]
    public void Scheduled_DiffersFromEveryOtherStatus()
    {
        byte[] others = [TransferStatus.Draft, TransferStatus.Active, TransferStatus.Expired, TransferStatus.DownloadLimit, TransferStatus.Deleted];
        Assert.DoesNotContain(TransferStatus.Scheduled, others);
    }
}
