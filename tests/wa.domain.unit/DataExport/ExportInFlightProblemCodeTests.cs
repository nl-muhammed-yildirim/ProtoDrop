using wa.domain.Errors;

namespace wa.domain.unit.DataExport;

/// <summary>US-047-01 (EC-047-6): EXPORT_IN_FLIGHT is a closed-list code mapped to 409; DataExportState wire names.</summary>
public class ExportInFlightProblemCodeTests
{
    [Fact]
    public void ExportInFlight_PinsWireStringStatusAndTitle()
    {
        Assert.Equal("EXPORT_IN_FLIGHT", ProblemCodes.ExportInFlight);
        Assert.Equal(409, ProblemCodes.StatusOf(ProblemCodes.ExportInFlight));
        Assert.Equal("Export already in progress", ProblemCodes.TitleOf(ProblemCodes.ExportInFlight));
    }

    [Theory]
    [InlineData((byte)0, "processing")]
    [InlineData((byte)1, "ready")]
    [InlineData((byte)2, "failed")]
    public void WireName_MapsKnownStates(byte state, string expected)
        => Assert.Equal(expected, wa.domain.DataExportState.WireName(state));

    [Fact]
    public void WireName_ThrowsForUnknownState()
        => Assert.Throws<ArgumentOutOfRangeException>(() => wa.domain.DataExportState.WireName(3));
}
