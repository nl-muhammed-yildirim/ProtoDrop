using wa.domain;

namespace wa.domain.unit.Plans;

/// <summary>T-031-03 (US-018-03, AC-018-4 price source): PlanPrice.Read is the one read model over Plan.PriceJson.</summary>
public class PlanPriceTests
{
    private static Plan Row(string json) => new() { Code = "pro", PriceJson = json };

    private static string Json(long monthly, string? monthlyId, long annual, string? annualId, string perSeat = "null")
    {
        static string Id(string? id) => id is null ? "null" : $"\"{id}\"";
        return $"{{\"currency\":\"USD\",\"monthly\":{{\"amount\":{monthly},\"stripePriceId\":{Id(monthlyId)}}},\"annual\":{{\"amount\":{annual},\"stripePriceId\":{Id(annualId)}}},\"perSeatMonthly\":{perSeat}}}";
    }

    [Fact]
    public void Read_ProRow_ForAnnual_ReturnsRowAnnualTier()
    {
        var price = PlanPrice.Read(Row(Json(900, "price_test_m", 9000, "price_test_a")));

        var annual = price.For(BillingInterval.Annual);
        Assert.Equal(9000, annual.Amount);
        Assert.Equal("price_test_a", annual.StripePriceId);

        var monthly = price.For(BillingInterval.Monthly);
        Assert.Equal(900, monthly.Amount);
        Assert.Equal("price_test_m", monthly.StripePriceId);
    }

    [Fact]
    public void Read_RowPriceChanged_ReturnsNewAmount_NoCodeChange()
    {
        var price = PlanPrice.Read(Row(Json(1200, "price_test_m", 9000, "price_test_a")));
        Assert.Equal(1200, price.For(BillingInterval.Monthly).Amount);
    }

    [Fact]
    public void IsWiredToStripe_NullOrBlankId_False_PriceId_True()
    {
        Assert.False(new PlanPriceTier { Amount = 1, StripePriceId = null }.IsWiredToStripe());
        Assert.False(new PlanPriceTier { Amount = 1, StripePriceId = " " }.IsWiredToStripe());
        Assert.True(new PlanPriceTier { Amount = 1, StripePriceId = "price_test_m" }.IsWiredToStripe());
    }

    [Fact]
    public void Read_BusinessRow_PerSeatMonthly_FromRow()
    {
        var price = PlanPrice.Read(Row(Json(1900, null, 19000, null, "1900")));
        Assert.Equal(1900, price.PerSeatMonthly);
    }

    [Fact]
    public void Read_NullPerSeat_IsNull()
    {
        var price = PlanPrice.Read(Row(Json(900, null, 9000, null)));
        Assert.Null(price.PerSeatMonthly);
    }

    [Fact]
    public void Read_InvalidJson_Throws()
    {
        Assert.ThrowsAny<Exception>(() => PlanPrice.Read(Row("not json")));
        var ex = Assert.Throws<InvalidOperationException>(() => PlanPrice.Read(Row("null")));
        Assert.Contains("pro", ex.Message);
    }

    [Fact]
    public void For_UnknownInterval_Throws()
    {
        var price = PlanPrice.Read(Row(Json(900, null, 9000, null)));
        Assert.Throws<ArgumentOutOfRangeException>(() => price.For((BillingInterval)99));
    }
}
