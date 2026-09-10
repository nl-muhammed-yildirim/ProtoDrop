namespace wa.api.Pipeline;

/// <summary>A single entry of the Problem+JSON <c>errors[]</c> payload (TA-4.1.3): one failed field, one message.</summary>
public sealed record FieldError(string Field, string Message);