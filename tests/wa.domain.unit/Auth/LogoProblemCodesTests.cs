using wa.domain.Errors;

namespace wa.domain.unit.Auth;

/// <summary>US-021-04 (AC-021-4, FR-021-5): LOGO_TOO_LARGE and LOGO_TYPE_INVALID are closed-list codes with pinned status and title.</summary>
public class LogoProblemCodesTests
{
    [Fact]
    public void LogoTooLarge_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("LOGO_TOO_LARGE", ProblemCodes.LogoTooLarge);
        Assert.Equal(422, ProblemCodes.StatusOf(ProblemCodes.LogoTooLarge));
        Assert.Equal("Logo too large", ProblemCodes.TitleOf(ProblemCodes.LogoTooLarge));
    }

    [Fact]
    public void LogoTypeInvalid_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("LOGO_TYPE_INVALID", ProblemCodes.LogoTypeInvalid);
        Assert.Equal(415, ProblemCodes.StatusOf(ProblemCodes.LogoTypeInvalid));
        Assert.Equal("Logo type not supported", ProblemCodes.TitleOf(ProblemCodes.LogoTypeInvalid));
    }
}
