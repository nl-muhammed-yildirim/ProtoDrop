namespace wa.domain.Transfers;

/// <summary>US-045-01 (FR-045-1, FR-045-6, EC-045-3): the transfer search term. Pure — no EF, no ASP.NET.</summary>
public static class SearchTerm
{
    /// <summary>FR-045-1: 1..100 chars after trim. Input-width constant (ORG_NAME_MAX_LENGTH precedent), not a plan limit.</summary>
    public const int MaxLength = 100;

    /// <summary>FR-045-3 / EC-045-6: relevance = max of the matched fields.</summary>
    public const int RankFileName = 3;
    /// <summary>The rank recipient constant; value <c>2</c>.</summary>
    public const int RankRecipient = 2;
    /// <summary>The rank sender constant; value <c>1</c>.</summary>
    public const int RankSender = 1;

    /// <summary>Trim; null/whitespace → null (no search). Over MaxLength after trim → SearchTermTooLongException (→ 400 Q_TOO_LONG).
    /// Characters are never interpreted: '*', '~', '"' stay literal (FR-045-6).</summary>
    /// <param name="raw">The raw.</param>
    /// <returns>The resulting text.</returns>
    /// <exception cref="SearchTermTooLongException">Thrown when a search term too long failure is detected.</exception>
    public static string? Normalize(string? raw)
    {
        var term = raw?.Trim();
        if (string.IsNullOrEmpty(term)) return null;
        if (term.Length > MaxLength)
            throw new SearchTermTooLongException($"q must be at most {MaxLength} characters.");
        return term;
    }
}
