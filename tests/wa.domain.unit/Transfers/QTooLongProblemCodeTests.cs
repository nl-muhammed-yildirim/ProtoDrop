using wa.domain.Errors;

namespace wa.domain.unit.Transfers;

/// <summary>US-045-01 (EC-045-3, AC-045-3): Q_TOO_LONG is a closed-list code mapped to 400.</summary>
public class QTooLongProblemCodeTests
{
    [Fact]
    public void QTooLong_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("Q_TOO_LONG", ProblemCodes.QTooLong);
        Assert.Equal(400, ProblemCodes.StatusOf(ProblemCodes.QTooLong));
        Assert.Equal("Search query too long", ProblemCodes.TitleOf(ProblemCodes.QTooLong));
    }
}
