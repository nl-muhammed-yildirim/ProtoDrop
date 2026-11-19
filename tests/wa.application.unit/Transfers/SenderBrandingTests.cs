using wa.application.Limits;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// F-PRF-001 US-021-02: shared branding gate + email sender-line composer.
public class SenderBrandingTests
{
    [Fact]
    public void Compose_OrgAndName_JoinsWithEmDash() =>
        Assert.Equal("Studio Nova — Anna K.", SenderBranding.ComposeEmailSenderLine("Anna K.", "Studio Nova"));

    [Fact]
    public void Compose_OrgOnly_BlankName_ReturnsOrg() =>
        Assert.Equal("Studio Nova", SenderBranding.ComposeEmailSenderLine("  ", "Studio Nova"));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Compose_NoOrg_ReturnsNameUnchanged(string? org) =>
        Assert.Equal("Anna K.", SenderBranding.ComposeEmailSenderLine("Anna K.", org));

    [Fact]
    public async Task Resolve_FreePlan_ReturnsNull()
    {
        var store = new Store(new SenderBrandingRow("free", "Studio Nova", null));
        Assert.Null(await SenderBranding.ResolveOrgNameAsync(store, new Features(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_GuestOwner_ReturnsNull_StoreNotCalled()
    {
        var store = new Store(new SenderBrandingRow("pro", "Studio Nova", null));
        Assert.Null(await SenderBranding.ResolveOrgNameAsync(store, new Features(), null, CancellationToken.None));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Resolve_ProWithOrg_ReturnsTrimmed()
    {
        var store = new Store(new SenderBrandingRow("pro", "  Studio Nova ", null));
        Assert.Equal("Studio Nova", await SenderBranding.ResolveOrgNameAsync(store, new Features(), Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class Store(SenderBrandingRow? row) : ISenderBrandingStore
    {
        public int Calls { get; private set; }
        public Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(row);
        }
    }

    private sealed class Features : IFeaturesProvider
    {
        public Task<PlanFeatures> Resolve(string? planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanFeatures { Branding = planCode is "pro" or "business" });
    }
}
