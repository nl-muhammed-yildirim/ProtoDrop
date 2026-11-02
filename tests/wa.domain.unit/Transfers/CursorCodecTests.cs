using wa.domain.Transfers;

namespace wa.domain.unit.Transfers;

/// <summary>
/// T-009-01 (TA-4.1.4 pin): cursor = base64("{createdAtUtc:O}:{id:N}").
/// Pure codec tests — encode/decode round-trip + malformed input → CursorFormatException (endpoint maps it to 400 VALIDATION).
/// </summary>
public class CursorCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var createdAtUtc = new DateTime(2026, 10, 30, 18, 45, 12, 345, DateTimeKind.Utc);
        var id = Guid.NewGuid();

        var encoded = CursorCodec.Encode(createdAtUtc, id);

        (var decodedTime, var decodedId) = CursorCodec.Decode(encoded);

        Assert.Equal(createdAtUtc, decodedTime);
        Assert.Equal(id, decodedId);
    }

    [Fact]
    public void Encode_Decode_RoundTrips_AcrossDifferentValues()
    {
        for (int i = 0; i < 5; i++)
        {
            var createdAtUtc = new DateTime(2026, 1, 1).AddSeconds(i * 37).ToUniversalTime();
            var id = Guid.NewGuid();

            (var decodedTime, var decodedId) = CursorCodec.Decode(CursorCodec.Encode(createdAtUtc, id));

            Assert.Equal(createdAtUtc, decodedTime);
            Assert.Equal(id, decodedId);
        }
    }

    [Fact]
    public void Decode_MalformedBase64_Throws_CursorFormatException()
    {
        Assert.Throws<CursorFormatException>(() => CursorCodec.Decode("!!!not-base64!!!"));
    }

    [Fact]
    public void Decode_MissingColonSeparator_Throws_CursorFormatException()
    {
        var noColon = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("abc123def4"));
        Assert.Throws<CursorFormatException>(() => CursorCodec.Decode(noColon));
    }

    [Fact]
    public void Decode_EmptyOrTrailingColon_Throws_CursorFormatException()
    {
        // ":" → both parts empty
        Assert.Throws<CursorFormatException>(() => CursorCodec.Decode(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(":"))));

        // "foo:" → id part empty
        Assert.Throws<CursorFormatException>(() => CursorCodec.Decode(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("foo:"))));
    }

    [Fact]
    public void Decode_UnparseableTimestamp_Throws_CursorFormatException()
    {
        var bad = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-a-timestamp:11223344556677889900aabbccdd"));
        Assert.Throws<CursorFormatException>(() => CursorCodec.Decode(bad));
    }

    [Fact]
    public void Decode_UnparseableGuid_Throws_CursorFormatException()
    {
        var bad = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("2026-10-30T18:45:12.345Z:not-a-guid"));
        Assert.Throws<CursorFormatException>(() => CursorCodec.Decode(bad));
    }
}
