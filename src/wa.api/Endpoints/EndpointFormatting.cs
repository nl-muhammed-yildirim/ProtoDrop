namespace wa.api.Endpoints;

/// <summary>Formatting helpers shared by several endpoint groups.</summary>
internal static class EndpointFormatting
{
    // EF reads datetime2 back as Kind=Unspecified (no trailing Z → browsers parse as local time); US-047 wire timestamps are UTC by contract.
    /// <summary>Marks a database <see cref="DateTime"/> as UTC (SQL Server returns Kind=Unspecified) so it serialises with a trailing Z.</summary>
    /// <param name="value">The value read from the database.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    public static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
