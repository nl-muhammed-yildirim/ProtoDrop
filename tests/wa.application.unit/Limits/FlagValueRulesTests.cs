using wa.application.Limits;

namespace wa.application.unit.Limits;

// US-044-01 (FR-044-5, EC-044-1, EC-044-5): typed flag value rules.
public class FlagValueRulesTests
{
    [Theory]
    [InlineData("limits.free.maxTransferSize", FlagValueRules.IntegerType)]
    [InlineData("limits.pro.retentionDays", FlagValueRules.IntegerType)]
    [InlineData("feature.ads", FlagValueRules.BooleanType)]
    [InlineData("limits.free.ads", FlagValueRules.BooleanType)]
    [InlineData("limits.free.nonexistent", FlagValueRules.BooleanType)]
    public void TypeOf_DerivesFromKeyShape(string key, string expected) =>
        Assert.Equal(expected, FlagValueRules.TypeOf(key));

    [Theory]
    [InlineData("limits.free.maxTransferSize", "10737418240")]
    [InlineData("limits.free.maxTransferSize", "1")]
    [InlineData("limits.business.maxDownloads", "-1")]
    [InlineData("limits.business.activeTransfersMax", "-1")]
    [InlineData("limits.business.maxZipSize", "9223372036854775807")]
    [InlineData("limits.free.maxDownloads", "2147483647")]
    [InlineData("feature.ads", "true")]
    [InlineData("feature.ads", "false")]
    [InlineData("limits.free.ads", "true")]
    public void IsValid_AcceptsValidValues(string key, string value) =>
        Assert.True(FlagValueRules.IsValid(key, value));

    [Theory]
    [InlineData("limits.free.maxTransferSize", "abc")]
    [InlineData("limits.free.maxTransferSize", "\"abc\"")]
    [InlineData("limits.free.maxTransferSize", "0")]
    [InlineData("limits.free.maxTransferSize", "-1")]
    [InlineData("limits.free.maxTransferSize", "-5")]
    [InlineData("limits.free.maxTransferSize", "1e10")]
    [InlineData("limits.free.maxTransferSize", "10.5")]
    [InlineData("limits.free.maxTransferSize", "")]
    [InlineData("limits.free.maxDownloads", "3000000000")]
    [InlineData("limits.free.maxDownloads", "0")]
    [InlineData("feature.ads", "True")]
    [InlineData("feature.ads", "1")]
    [InlineData("feature.ads", "\"true\"")]
    [InlineData("limits.free.nonexistent", "5")]
    public void IsValid_RejectsInvalidValues(string key, string value) =>
        Assert.False(FlagValueRules.IsValid(key, value));

    [Fact]
    public void TryGetLimitField_ReturnsPlanAndField()
    {
        Assert.True(FlagValueRules.TryGetLimitField("limits.pro.retentionDays", out var plan, out var field));
        Assert.Equal("pro", plan);
        Assert.Equal("retentionDays", field);
    }

    [Theory]
    [InlineData("feature.ads")]
    [InlineData("limits.free.nonexistent")]
    [InlineData("limits.free")]
    [InlineData("limits.free.maxTransferSize.extra")]
    public void TryGetLimitField_FalseForNonLimitKeys(string key) =>
        Assert.False(FlagValueRules.TryGetLimitField(key, out _, out _));
}
