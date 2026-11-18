using wa.domain.Errors;

namespace wa.domain.unit.Flags;

/// <summary>US-044-01 (AC-044-3): FLAG_INVALID (400) and FLAG_UNKNOWN (404) are closed-list codes.</summary>
public class FlagProblemCodesTests
{
    [Fact]
    public void FlagInvalid_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("FLAG_INVALID", ProblemCodes.FlagInvalid);
        Assert.Equal(400, ProblemCodes.StatusOf(ProblemCodes.FlagInvalid));
        Assert.Equal("Invalid flag value", ProblemCodes.TitleOf(ProblemCodes.FlagInvalid));
    }

    [Fact]
    public void FlagUnknown_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("FLAG_UNKNOWN", ProblemCodes.FlagUnknown);
        Assert.Equal(404, ProblemCodes.StatusOf(ProblemCodes.FlagUnknown));
        Assert.Equal("Unknown flag", ProblemCodes.TitleOf(ProblemCodes.FlagUnknown));
    }

    [Fact]
    public void FeatureDisabled_PinsWireStringStatusAndTitle() // US-044-02 (AC-044-2)
    {
        Assert.Equal("FEATURE_DISABLED", ProblemCodes.FeatureDisabled);
        Assert.Equal(404, ProblemCodes.StatusOf(ProblemCodes.FeatureDisabled));
        Assert.Equal("Feature disabled", ProblemCodes.TitleOf(ProblemCodes.FeatureDisabled));
    }
}
