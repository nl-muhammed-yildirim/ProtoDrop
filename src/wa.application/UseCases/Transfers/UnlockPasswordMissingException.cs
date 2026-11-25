namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-003-04 (D1): the password field is missing from the unlock request body. The endpoint catches this and throws
/// BusinessException(ProblemCodes.Validation) with errors[] — keeps the closed code list intact (same precedent as TransferValidationException).
/// </summary>
public sealed class UnlockPasswordMissingException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UnlockPasswordMissingException"/> class.</summary>
    /// <param name="message">Description of the missing field, returned as the validation detail.</param>
    public UnlockPasswordMissingException(string message) : base(message) { }
}
