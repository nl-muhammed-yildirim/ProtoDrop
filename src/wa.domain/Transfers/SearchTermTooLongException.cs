namespace wa.domain.Transfers;

/// <summary>US-045-01 (EC-045-3): the endpoint maps this to 400 Q_TOO_LONG via BusinessException.</summary>
public sealed class SearchTermTooLongException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SearchTermTooLongException"/> class.</summary>
    /// <param name="message">Message stating the allowed maximum length.</param>
    public SearchTermTooLongException(string message) : base(message) { }
}
