using System.Text;
using wa.domain.Transfers;

namespace wa.domain.unit.Transfers;

/// <summary>US-045-01 (FR-045-3, TA-4.1.4): SearchCursorCodec — round-trip + every malformed shape → CursorFormatException.</summary>
public class SearchCursorCodecTests
{
    private static string B64(string inner) => Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));

    [Fact]
    public void RoundTrip_TermWithPipeAndSpaces_RankUtcId()
    {
        var term = "a|b c@d.com";   // '|' and spaces in the term — it is last, so Split('|', 4) keeps it whole.
        var utc = new DateTime(2026, 11, 21, 15, 30, 7, DateTimeKind.Utc);
        var id = Guid.NewGuid();

        var decoded = SearchCursorCodec.Decode(SearchCursorCodec.Encode(term, 2, utc, id));

        Assert.Equal((term, 2, utc, id), (decoded.Term, decoded.Rank, decoded.CreatedAtUtc, decoded.Id));
    }

    [Fact]
    public void NotBase64_Throws() =>
        Assert.Throws<CursorFormatException>(() => SearchCursorCodec.Decode("!!!not-base64!!!"));

    // A plain list cursor has no '|' at all (base64("{O}:{N}")) → wrong shape.
    [Fact]
    public void PlainListCursor_ThrowsWrongShape()
    {
        var utc = new DateTime(2026, 11, 21, 15, 30, 7, DateTimeKind.Utc);
        Assert.Throws<CursorFormatException>(() => SearchCursorCodec.Decode(CursorCodec.Encode(utc, Guid.NewGuid())));
    }

    [Theory]
    [InlineData("0")]   // rank must be >= 1 (file=3 > recipient=2 > sender=1)
    [InlineData("x")]
    public void BadRank_Throws(string rank) =>
        Assert.Throws<CursorFormatException>(() => SearchCursorCodec.Decode(B64($"{rank}|{new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc):O}|{Guid.NewGuid():N}|term")));

    [Fact]
    public void EmptyTerm_Throws() =>
        Assert.Throws<CursorFormatException>(() => SearchCursorCodec.Decode(B64($"3|{new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc):O}|{Guid.NewGuid():N}|")));

    [Fact]
    public void BadGuid_Throws() =>
        Assert.Throws<CursorFormatException>(() => SearchCursorCodec.Decode(B64($"3|{new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc):O}|not-a-guid-n-form|term")));

    [Fact]
    public void BadTimestamp_Throws() =>
        Assert.Throws<CursorFormatException>(() => SearchCursorCodec.Decode(B64($"3|garbage|{Guid.NewGuid():N}|term")));
}
