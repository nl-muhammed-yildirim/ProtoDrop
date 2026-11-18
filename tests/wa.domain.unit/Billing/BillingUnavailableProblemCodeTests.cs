using wa.domain.Errors;

namespace wa.domain.unit.Billing;

/// <summary>US-019-01 (AC-019-5): BILLING_UNAVAILABLE is a closed-list code mapped to 503.</summary>
public class BillingUnavailableProblemCodeTests
{
    [Fact]
    public void BillingUnavailable_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("BILLING_UNAVAILABLE", ProblemCodes.BillingUnavailable);
        Assert.Equal(503, ProblemCodes.StatusOf(ProblemCodes.BillingUnavailable));
        Assert.Equal("Billing unavailable", ProblemCodes.TitleOf(ProblemCodes.BillingUnavailable));
    }
}
