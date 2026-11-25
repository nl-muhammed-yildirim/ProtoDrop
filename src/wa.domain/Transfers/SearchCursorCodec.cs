using System.Globalization;
using System.Text;

namespace wa.domain.Transfers;

/// <summary>US-045-01 (FR-045-3): opaque search cursor = base64("{rank}|{createdAtUtc:O}|{id:N}|{term}") of the last row.
/// The term is last (it may contain '|'). Any malformed input → CursorFormatException (endpoint → 400 VALIDATION).</summary>
public static class SearchCursorCodec
{
    /// <summary>Encodes the text value.</summary>
    /// <param name="term">The term.</param>
    /// <param name="rank">The rank.</param>
    /// <param name="createdAtUtc">The created at UTC.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The resulting text.</returns>
    public static string Encode(string term, int rank, DateTime createdAtUtc, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rank.ToString(CultureInfo.InvariantCulture)}|{createdAtUtc:O}|{id:N}|{term}"));

    /// <summary>Decodes the value tuple of text value.</summary>
    /// <param name="cursor">The cursor.</param>
    /// <returns>A tuple of the term, rank, created at UTC, ID values.</returns>
    /// <exception cref="CursorFormatException">Thrown when a cursor format failure is detected.</exception>
    public static (string Term, int Rank, DateTime CreatedAtUtc, Guid Id) Decode(string cursor)
    {
        string raw;
        try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)); }
        catch (FormatException) { throw new CursorFormatException("cursor is not valid base64."); }

        var parts = raw.Split('|', 4);
        if (parts.Length != 4 || parts[3].Length == 0)
            throw new CursorFormatException("search cursor has the wrong shape.");
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rank) || rank < 1)
            throw new CursorFormatException("search cursor has an invalid rank.");
        if (!DateTime.TryParseExact(parts[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAtUtc))
            throw new CursorFormatException("search cursor has an unparseable timestamp.");
        if (!Guid.TryParseExact(parts[2], "N", out var id))
            throw new CursorFormatException("search cursor has an unparseable transfer id.");
        return (parts[3], rank, createdAtUtc, id);
    }
}
