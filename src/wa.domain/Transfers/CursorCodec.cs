namespace wa.domain.Transfers;

/// <summary>
/// T-009-01 (TA-4.1.4 pin): cursor = base64("{createdAtUtc:O}:{id:N}") — the last row's CreatedAtUtc + Id.
/// Pure static class (no EF, no ASP.NET) so tests/wa.domain.unit can cover encode/decode without infrastructure.
/// </summary>
public static class CursorCodec
{
    /// <summary>Encodes a cursor from the last row of a page.</summary>
    /// <param name="createdAtUtc">CreatedAtUtc of the last row on the page.</param>
    /// <param name="id">Id of the last row on the page; breaks ties between equal timestamps.</param>
    /// <returns>The opaque base64 cursor to hand to the client.</returns>
    public static string Encode(DateTime createdAtUtc, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{createdAtUtc:O}:{id:N}"));

    /// <summary>
    /// Decodes a cursor into (createdAtUtc, id). Throws <see cref="CursorFormatException"/> on any malformed input —
    /// the endpoint maps that to 400 VALIDATION (existing ProblemCodes member; no new closed-list entry).
    /// </summary>
    /// <param name="cursor">Opaque cursor previously produced by <see cref="Encode"/>.</param>
    /// <returns>The CreatedAtUtc and Id of the last row of the previous page.</returns>
    /// <exception cref="CursorFormatException">Thrown when a cursor format failure is detected.</exception>
    public static (DateTime CreatedAtUtc, Guid Id) Decode(string cursor)
    {
        string? raw;
        try
        {
            raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            throw new CursorFormatException("cursor is not valid base64.");
        }

        // The O-format timestamp contains colons (e.g. 2026-10-30T18:45:12Z), so the separator is the LAST colon —
        // the id part ({id:N} = 32 hex chars) never contains one.
        var separator = raw?.LastIndexOf(':') ?? -1;
        if (separator <= 0 || separator == (raw?.Length ?? 0) - 1)
            throw new CursorFormatException("cursor has the wrong shape.");

        var createdPart = raw![..separator];
        var idPart = raw[(separator + 1)..];

        if (!DateTime.TryParseExact(createdPart, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdAtUtc))
            throw new CursorFormatException("cursor has an unparseable timestamp.");
        if (!Guid.TryParse(idPart, out var id))
            throw new CursorFormatException("cursor has an unparseable transfer id.");

        return (createdAtUtc, id);
    }
}
