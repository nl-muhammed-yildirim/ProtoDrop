using wa.application.UseCases.Transfers;
using Xunit;

namespace wa.application.unit.Transfers;

/// <summary>CHORE-zip-route-prefix: pins the exact URL the API posts to when it triggers f-zip.</summary>
public class ZipHandOffTests
{
    private static readonly Guid Id = Guid.Parse("0123456789abcdef0123456789abcdef");

    [Fact]
    public void BuildTriggerUrl_AppendsApiPrefixAndNFormatId()
    {
        var url = ZipHandOff.BuildTriggerUrl("http://host.docker.internal:8081", Id);

        Assert.Equal("http://host.docker.internal:8081/api/zip/0123456789abcdef0123456789abcdef", url);
    }

    [Fact]
    public void BuildTriggerUrl_TrimsTrailingSlash()
    {
        var url = ZipHandOff.BuildTriggerUrl("https://fa.example.net/", Id);

        Assert.Equal("https://fa.example.net/api/zip/0123456789abcdef0123456789abcdef", url);
    }

    [Fact]
    public void BuildTriggerUrl_DoesNotDoublePrefix_WhenBaseHasNoApi()
    {
        var url = ZipHandOff.BuildTriggerUrl("https://fa.example.net", Id);

        Assert.Equal(1, url.Split("/api/zip/").Length - 1);
        Assert.DoesNotContain("/api/api/", url);
    }

    [Fact]
    public void Route_IsZipTransferIdTemplate()
    {
        Assert.Equal("zip/{transferId}", ZipHandOff.Route);
        Assert.Equal("api", ZipHandOff.RoutePrefix);
    }
}
