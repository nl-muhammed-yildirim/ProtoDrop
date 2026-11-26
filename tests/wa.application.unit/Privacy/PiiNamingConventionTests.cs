using System.Reflection;
using wa.application.unit.Privacy.Fixtures;
using wa.domain.Privacy;

namespace wa.application.unit.Privacy;

/// <summary>F-XCT-005 US-048-02 (AC-048-2/3): PII-looking columns must carry [Pii] or a reviewed [NotPii(reason)].</summary>
public class PiiNamingConventionTests
{
    private static IEnumerable<PropertyInfo> Columns<T>() => typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string));

    [Fact]
    public void UnmarkedEmail_Fixture_FailsNamingTheProperty_AndTheFix()
    {
        var violations = PiiConvention.NamingViolations(Columns<UnmarkedEmailFixture>());
        var v = Assert.Single(violations);
        Assert.Contains("UnmarkedEmailFixture.Email", v);
        Assert.Contains("[Pii]", v);
    }

    [Fact]
    public void MarkedEmail_Fixture_Passes() =>
        Assert.Empty(PiiConvention.NamingViolations(Columns<MarkedEmailFixture>()));

    [Fact]
    public void SurpriseName_Fixture_Fails() =>
        Assert.Single(PiiConvention.NamingViolations(Columns<SurpriseNameFixture>()));

    [Fact]
    public void NotPii_Fixture_Passes() =>
        Assert.Empty(PiiConvention.NamingViolations(Columns<NotPiiNameFixture>()));

    [Theory]
    [InlineData("Email", true)]
    [InlineData("SenderEmail", true)]
    [InlineData("OriginalName", true)]
    [InlineData("Address", true)]
    [InlineData("IpHash", true)]
    [InlineData("Description", false)]
    [InlineData("Recipient", false)]
    [InlineData("Domain", false)]
    public void NameRule_IsWordBased(string name, bool expected) =>
        Assert.Equal(expected, PiiConvention.IsPiiLookingName(name));

    [Fact]
    public void EveryModelColumn_IsMarkedOrClean() =>
        Assert.Empty(PiiConvention.NamingViolations(PiiConvention.ModelStringColumns()));

    [Fact]
    public void NotPiiEscapes_ArePinned()
    {
        var escapes = PiiConvention.ModelStringColumns()
            .Where(p => p.GetCustomAttribute<NotPiiAttribute>() is not null)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .Order()
            .ToArray();
        Assert.Equal(["DownloadEvent.IpHash", "Plan.Name"], escapes);
    }
}
