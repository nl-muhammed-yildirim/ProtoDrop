using wa.domain.Errors;

namespace wa.api.Middleware;

/// <summary>
/// A business error with its own closed TA-4.1.3 code (EC-056-2) — the pipeline maps it to its canonical status + code,
/// never swallowing it into 500 INTERNAL. Status is derived from the code table so callers cannot drift.
/// </summary>
public sealed class BusinessException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="BusinessException"/> class.</summary>
    /// <param name="code">The code.</param>
    /// <param name="details">The details.</param>
    public BusinessException(string code, string details)
        : base(details) => Code = code;

    /// <summary>Preserves the inner exception so its stack trace survives — the wire shape still comes from <see cref="Code"/> + the pipeline.</summary>
    /// <param name="code">The code.</param>
    /// <param name="details">The details.</param>
    /// <param name="innerException">The inner exception.</param>
    public BusinessException(string code, string details, Exception innerException)
        : base(details, innerException) => Code = code;

    /// <summary>A closed TA-4.1.3 code (validated against the list).</summary>
    public string Code { get; }

    /// <summary>Optional per-field validation errors (TA-4.1.3 optional <c>errors[]</c>) — present only for VALIDATION-shaped responses.</summary>
    public IReadOnlyList<ProblemFieldError>? Errors { get; set; }

    /// <summary>Gets the status of the business exception.</summary>
    internal int Status => ProblemCodes.StatusOf(Code);
}
