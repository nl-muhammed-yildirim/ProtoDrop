using System.Security.Cryptography;

namespace wa.domain.ValueObjects;

/// <summary>
/// 8-char public link identifier (TA-9.6): the T-002-01 spec literal charset — authoritative over the doc's "no 0/O/1/I" phrasing (the literal excludes exactly i/l/o/u and keeps 0).
/// Generation lives in FinalizeTransferCommandHandler (T-001-10): mint a candidate; on collision with an existing <see cref="Transfer.LinkId"/> regenerate once; second collision → 500 + INTERNAL (AC-002-4). The DB's UQ_Transfer_LinkId is the uniqueness guard — no in-memory set here.
/// </summary>
/// <param name="Value">The 8-character link text drawn from <see cref="Charset"/>.</param>
public readonly record struct LinkId(string Value)
{
    /// <summary>The T-002-01 spec literal: Crockford base32 minus i,l,o,u (32-char alphabet).</summary>
    public const string Charset = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>Length: exactly 8 — 40 bits of entropy (TA-9.6; enumeration is not a practical threat).</summary>
    public const int Length = 8;

    /// <summary>Mint a random candidate. Cryptographic RNG — the link is the only bearer secret of the M0 anonymous surface.</summary>
    /// <returns>A new random <see cref="LinkId"/>; uniqueness is not checked here.</returns>
    public static LinkId Mint()
    {
        var bytes = RandomNumberGenerator.GetBytes(Length); // one byte per char, mod 32 over a power-of-two alphabet → unbiased.
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Charset[bytes[i] % Charset.Length];
        return new LinkId(new string(chars));
    }

    /// <summary>The raw link text — the value stored in Transfer.LinkId and rendered into {origin}/t/{linkId}.</summary>
    /// <returns>The raw 8-character link text.</returns>
    public override string ToString() => Value;
}
