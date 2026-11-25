namespace wa.domain.Transfers;

/// <summary>T-009-01: a cursor failed to decode — the endpoint maps this to 400 VALIDATION via BusinessException.</summary>
public sealed class CursorFormatException : Exception
{
    public CursorFormatException(string message) : base(message) { }
}
