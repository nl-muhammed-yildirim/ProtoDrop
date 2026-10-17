using wa.domain.ValueObjects;

namespace wa.domain.unit.ValueObjects;

public class LinkIdTests
{
    [Fact]
    public void Mint_ReturnsExactly8Characters()
    {
        for (var i = 0; i < 100; i++)
            Assert.Equal(LinkId.Length, LinkId.Mint().Value.Length);
    }

    [Fact]
    public void Mint_OnlyUsesCrockfordCharset()
    {
        var allowed = new HashSet<char>(LinkId.Charset);
        for (var i = 0; i < 1000; i++)
        {
            var value = LinkId.Mint().Value;
            Assert.All(value, c => Assert.Contains(c, allowed));
        }
    }

    [Fact]
    public void Mint_Distribution_AllCharsAppearInEachPosition()
    {
        const int samples = 1000;
        var seen = new bool[LinkId.Length][];
        for (var pos = 0; pos < LinkId.Length; pos++)
            seen[pos] = new bool[LinkId.Charset.Length];

        for (var i = 0; i < samples; i++)
        {
            var value = LinkId.Mint().Value;
            for (var pos = 0; pos < LinkId.Length; pos++)
                seen[pos][LinkId.Charset.IndexOf(value[pos])] = true;
        }

        for (var pos = 0; pos < LinkId.Length; pos++)
            Assert.All(seen[pos], s => Assert.True(s));
    }

    [Fact]
    public void Mint_ConsecutiveCalls_ProduceDifferentValues()
    {
        var a = LinkId.Mint();
        var b = LinkId.Mint();
        Assert.NotEqual(a.Value, b.Value);
    }
}
