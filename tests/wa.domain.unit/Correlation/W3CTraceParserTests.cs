using wa.domain.Correlation;

namespace wa.domain.unit.Correlation;

/// <summary>T-056-01 (AC-056-1): W3C traceparent validation — valid v00 reused, malformed/wrong-version treated as absent (EC-056-1), correlationId = first 8 hex of trace-id.</summary>
public class W3CTraceParserTests
{
    private const string Valid = "00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07";

    // ---- valid incoming header → reused verbatim (T-056-01 exit check 2) ----

    [Fact]
    public void Parse_ValidTraceparent_ReturnsFullFourFields()
    {
        var tp = W3CTraceParser.Parse(Valid);

        Assert.NotNull(tp);
        Assert.Equal("a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6", tp!.TraceId);
        Assert.Equal("0af1bc2d3e4f5a6b", tp.ParentId);
        Assert.Equal("00", tp.Version);
        Assert.Equal("07", tp.Flags);
    }

    [Fact]
    public void Parse_ValidTraceparent_EchoesExactHeaderValue() =>
        Assert.Equal(Valid, W3CTraceParser.Parse(Valid)!.ToHeader());

    // ---- absent header → null (middleware then generates; T-056-01 exit check 1) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_AbsentOrBlank_ReturnsNull(string? value) => Assert.Null(W3CTraceParser.Parse(value));

    // ---- malformed / wrong version → treated as absent, never crash (EC-056-1; T-056-01 exit check 3) ----

    [Theory]
    [InlineData("01-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07")] // wrong version — 01 is not SupportedVersion
    [InlineData("zz-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07")] // non-hex version
    [InlineData("A0-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07")] // uppercase hex version is not the "00" form
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d-0af1bc2d3e4f5a6b-07")] // trace-id 31 chars
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d67-0af1bc2d3e4f5a6b-07")] // trace-id 33 chars
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b0-07")] // parent-id 17 chars
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6-07")] // parent-id 15 chars
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b")] // only three parts
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07-extra")] // five parts — flags become "07-extra", not 2 hex chars
    [InlineData("00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5g6-0af1bc2d3e4f5a6b-07")] // non-hex char in trace-id
    [InlineData("00-a1b2c3d4E5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07")] // uppercase hex in trace-id — lowercase form required at M0
    [InlineData("00--0af1bc2d3e4f5a6b-07")] // empty trace-id segment (double dash)
    public void Parse_MalformedTreatedAsAbsent_ReturnsNull(string value) => Assert.Null(W3CTraceParser.Parse(value));

    [Fact]
    public void Parse_AllZeroTraceId_IsInvalid() =>
        Assert.Null(W3CTraceParser.Parse("00-00000000000000000000000000000000-0af1bc2d3e4f5a6b-07"));

    // ---- trace-id accessor mirrors Parse (FR: ParseTraceId is the fast path) ----

    [Fact]
    public void ParseTraceId_Valid_ReturnsTraceIdOnly() =>
        Assert.Equal("a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6", W3CTraceParser.ParseTraceId(Valid));

    [Fact]
    public void ParseTraceId_Malformed_ReturnsNull() =>
        Assert.Null(W3CTraceParser.ParseTraceId("garbage"));

    // ---- NewTraceId + generated traceparent shape (middleware's absent-header path) ----

    [Fact]
    public void NewTraceId_IsExactly32LowercaseHexChars()
    {
        var id = W3CTraceParser.NewTraceId();

        Assert.Equal(32, id.Length);
        Assert.All(id, c => Assert.True(char.IsDigit(c) || (c is >= 'a' and <= 'f')));
    }

    [Fact]
    public void NewTraceId_IsRandomAcrossCalls() => Assert.NotEqual(W3CTraceParser.NewTraceId(), W3CTraceParser.NewTraceId());

    // ---- correlationId = exactly the first 8 hex of trace-id (TA-10.1 — not the full trace-id) ----

    [Fact]
    public void CorrelationId_IsExactlyFirstEightHexChars() =>
        Assert.Equal("a1b2c3d4", W3CTraceParser.Parse(Valid)!.CorrelationId);

    // ---- ambient context (middleware sets it once per request; tests exercise Set directly — internal via InternalsVisibleTo? no: domain has no test visibility yet, so cover the public surface only) ----

    [Fact]
    public void CorrelationContext_OutsideRequest_FallsBackToFreshEightHexId()
    {
        var id = CorrelationContext.CorrelationId;

        Assert.Equal(8, id.Length);
        Assert.All(id, c => Assert.True(char.IsDigit(c) || (c is >= 'a' and <= 'f')));
    }
}
