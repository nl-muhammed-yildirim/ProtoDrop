using wa.domain.Transfers;

namespace wa.domain.unit.Transfers;

/// <summary>US-045-01 (FR-045-1, FR-045-6, EC-045-3): SearchTerm.Normalize — trim/null/length rules + rank constants.</summary>
public class SearchTermTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespace_ReturnsNull(string? raw) => Assert.Null(SearchTerm.Normalize(raw));

    [Fact]
    public void Trimmed() => Assert.Equal("financial", SearchTerm.Normalize("  financial  "));

    [Fact]
    public void Exactly100Chars_IsReturned()
    {
        var term = new string('a', SearchTerm.MaxLength);
        Assert.Equal(term, SearchTerm.Normalize(term));
    }

    [Fact]
    public void OneHundredOneChars_ThrowsSearchTermTooLong() =>
        Assert.Throws<SearchTermTooLongException>(() => SearchTerm.Normalize(new string('a', SearchTerm.MaxLength + 1)));

    // The trim happens FIRST: "  " + 100 chars + "  " is exactly MaxLength after trim → OK.
    [Fact]
    public void PaddedToExactlyMaxLengthAfterTrim_IsReturned() =>
        Assert.Equal(new string('a', SearchTerm.MaxLength), SearchTerm.Normalize("  " + new string('a', SearchTerm.MaxLength) + "  "));

    // FR-045-6: operators stay literal — Normalize never interprets them.
    [Theory]
    [InlineData("report *")]
    [InlineData("~\"x\"")]
    public void Operators_AreLiteral(string term) => Assert.Equal(term, SearchTerm.Normalize(term));

    [Fact]
    public void RankConstants_FileGreaterThanRecipientGreaterThanSender()
    {
        Assert.True(SearchTerm.RankFileName > SearchTerm.RankRecipient);
        Assert.True(SearchTerm.RankRecipient > SearchTerm.RankSender);
        Assert.Equal(3, SearchTerm.RankFileName);
        Assert.Equal(2, SearchTerm.RankRecipient);
        Assert.Equal(1, SearchTerm.RankSender);
    }
}
