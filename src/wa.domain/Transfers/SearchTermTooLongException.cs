namespace wa.domain.Transfers;

/// <summary>US-045-01 (EC-045-3): the endpoint maps this to 400 Q_TOO_LONG via BusinessException.</summary>
public sealed class SearchTermTooLongException : Exception
{
    public SearchTermTooLongException(string message) : base(message) { }
}
