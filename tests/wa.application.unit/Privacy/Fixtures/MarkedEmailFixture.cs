using wa.domain.Privacy;

namespace wa.application.unit.Privacy.Fixtures;

/// <summary>Green fixture: the same column as <see cref="UnmarkedEmailFixture"/> once it is marked (US-048-02).</summary>
public sealed class MarkedEmailFixture
{
    [Pii]
    public string Email { get; set; } = "";
}
