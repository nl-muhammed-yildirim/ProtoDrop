using wa.domain.Privacy;

namespace wa.application.unit.Privacy.Fixtures;

/// <summary>Green fixture: a PII-looking name explicitly cleared with a reason (US-048-02 alternative flow).</summary>
public sealed class NotPiiNameFixture
{
    [NotPii("product label")]
    public string Name { get; set; } = "";
}
