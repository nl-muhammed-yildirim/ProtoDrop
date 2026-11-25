namespace wa.domain.Transfers;

/// <summary>T-009-01: a cursor failed to decode — the endpoint maps this to 400 VALIDATION via BusinessException.</summary>
public sealed class CursorFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CursorFormatException"/> class.</summary>
    /// <param name="message">Description of why the cursor is invalid (never echoes the raw cursor).</param>
    public CursorFormatException(string message) : base(message) { }
}
