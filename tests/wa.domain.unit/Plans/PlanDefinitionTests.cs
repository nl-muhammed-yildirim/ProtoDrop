using wa.domain;

namespace wa.domain.unit.Plans;

/// <summary>T-031-01 (EC-018-2): PlanDefinition.Validate rejects malformed plan rows with a field-naming error.</summary>
public class PlanDefinitionTests
{
    private const string FreeLimits = "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}";
    private const string FreeFeatures = "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}";

    private static Plan Valid() => new() { Id = Guid.NewGuid(), Code = "free", Name = "Free", LimitsJson = FreeLimits, FeaturesJson = FreeFeatures, SortOrder = 1 };

    private static string Limits(string key, string value) =>
        FreeLimits.Replace(Pair(key), $"\"{key}\":{value}");

    // Finds the "key":value pair in the Free seed so a single value can be swapped.
    private static string Pair(string key)
    {
        var start = FreeLimits.IndexOf($"\"{key}\":", StringComparison.Ordinal);
        var end = FreeLimits.IndexOfAny([',', '}'], start);
        return FreeLimits[start..end];
    }

    [Fact]
    public void ValidRow_ReturnsNoErrors()
    {
        Assert.Empty(PlanDefinition.Validate(Valid()));
    }

    [Fact]
    public void LimitsJson_MissingKey_Errors()
    {
        var plan = Valid();
        plan.LimitsJson = FreeLimits.Replace("\"maxEmails\":20,", "");
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("maxEmails"));
    }

    [Fact]
    public void LimitsJson_UnknownKey_Errors()
    {
        var plan = Valid();
        plan.LimitsJson = FreeLimits.TrimEnd('}') + ",\"maxFoo\":1}";
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("maxFoo"));
    }

    [Theory]
    [InlineData("maxEmails", "\"5\"")]
    [InlineData("maxEmails", "1.5")]
    [InlineData("maxEmails", "true")]
    [InlineData("maxEmails", "3000000000")]
    [InlineData("retentionDays", "-1")]
    [InlineData("maxDownloads", "-2")]
    [InlineData("activeTransfersMax", "-2")]
    [InlineData("maxZipSize", "-1")]
    public void LimitsJson_BadValue_Errors(string key, string value)
    {
        var plan = Valid();
        plan.LimitsJson = Limits(key, value);
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains(key));
    }

    [Theory]
    [InlineData("maxDownloads", "-1")]
    [InlineData("activeTransfersMax", "-1")]
    [InlineData("maxZipSize", "9223372036854775807")]
    public void LimitsJson_AllowedSentinels_Pass(string key, string value)
    {
        var plan = Valid();
        plan.LimitsJson = Limits(key, value);
        Assert.Empty(PlanDefinition.Validate(plan));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void LimitsJson_NotAnObject_Errors(string json)
    {
        var plan = Valid();
        plan.LimitsJson = json;
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("LimitsJson"));
    }

    [Fact]
    public void FeaturesJson_MissingKey_Errors()
    {
        var plan = Valid();
        plan.FeaturesJson = "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false}";
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("sso_scim"));
    }

    [Fact]
    public void FeaturesJson_UnknownKey_Errors()
    {
        var plan = Valid();
        plan.FeaturesJson = FreeFeatures.TrimEnd('}') + ",\"teleport\":true}";
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("teleport"));
    }

    [Fact]
    public void FeaturesJson_StringValue_Errors()
    {
        var plan = Valid();
        plan.FeaturesJson = FreeFeatures.Replace("\"ads\":false", "\"ads\":\"true\"");
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("FeaturesJson.ads"));
    }

    [Fact]
    public void FeaturesJson_NotJson_Errors()
    {
        var plan = Valid();
        plan.FeaturesJson = "nope";
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("FeaturesJson"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")] // 33 chars
    [InlineData("pro.plus")]
    public void Code_Invalid_Errors(string code)
    {
        var plan = Valid();
        plan.Code = code;
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("Code"));
    }

    [Fact]
    public void Name_TooLong_Errors()
    {
        var plan = Valid();
        plan.Name = new string('n', 65);
        Assert.Contains(PlanDefinition.Validate(plan), e => e.Contains("Name"));
    }

    [Fact]
    public void MultipleProblems_ReportEachOne()
    {
        var plan = Valid();
        plan.Code = "";
        plan.Name = "";
        plan.LimitsJson = "x";
        plan.FeaturesJson = "y";
        Assert.True(PlanDefinition.Validate(plan).Count >= 4);
    }
}
