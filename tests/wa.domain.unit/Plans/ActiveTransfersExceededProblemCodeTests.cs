using wa.domain.Errors;

namespace wa.domain.unit.Plans;

/// <summary>US-020-02 (FR-020-2): ACTIVE_TRANSFERS_EXCEEDED is a closed-list code mapped to 409.</summary>
public class ActiveTransfersExceededProblemCodeTests
{
    [Fact]
    public void ActiveTransfersExceeded_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("ACTIVE_TRANSFERS_EXCEEDED", ProblemCodes.ActiveTransfersExceeded);
        Assert.Equal(409, ProblemCodes.StatusOf(ProblemCodes.ActiveTransfersExceeded));
        Assert.Equal("Active transfers limit reached", ProblemCodes.TitleOf(ProblemCodes.ActiveTransfersExceeded));
    }
}
