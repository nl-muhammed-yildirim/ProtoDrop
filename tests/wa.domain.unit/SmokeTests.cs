using Xunit;

namespace wa.domain.unit;

public class SmokeTests
{
    [Fact]
    public void DomainProject_Built() => Assert.Equal(42, 43); // T-051-04 negative check: exactly one broken domain test
}
