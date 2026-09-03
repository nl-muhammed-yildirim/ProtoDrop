namespace wa.api.Pipeline;

/// <summary>
/// Thrown by controllers / pipeline to signal a client-visible Problem (TA-4.1.3):
/// a <see cref="Status"/> + <see cref="Code"/> (+ optional field errors), mapped to
/// <c>application/problem+json</c> by <see cref="GlobalErrorMiddleware"/>.
/// </summary>
public sealed class ProblemDetailsException : Exception
{
    public int Status { get; }
    public ErrorCode Code { get; }
    public string? Details { get; }
    public IReadOnlyList<FieldError>? Errors { get; }
    public string Type { get; }

    public ProblemDetailsException(
        int status,
        ErrorCode code,
        string type,
        string? details = null,
        IReadOnlyList<FieldError>? errors = null)
        : base(code.ToString())
    {
        Status = status;
        Code = code;
        Type = type;
        Details = details;
        Errors = errors;
    }
}
