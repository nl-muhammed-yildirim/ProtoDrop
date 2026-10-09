using Xunit;

namespace wa.api.integration;

public class SmokeTests
{
    // Smoke only: no container, no API host startup yet (real coverage arrives with T-002+).
    [Fact]
    public void IntegrationProject_Built() => Assert.Equal(42, 42);
}
