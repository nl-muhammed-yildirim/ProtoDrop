using wa.application.Limits;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04: validation failure inside the search use case — same pattern as LoginValidationException (T-008-02).
/// The endpoint catches and maps to VALIDATION (400) via the closed code list (TA-4.1.3); no new code added.
/// </summary>
public sealed class SearchTransfersValidationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SearchTransfersValidationException"/> class.</summary>
    /// <param name="message">Description of the invalid filter combination, returned to the client as the validation detail.</param>
    public SearchTransfersValidationException(string message) : base(message) { }
}
