using wa.application.Limits;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04: validation failure inside the search use case — same pattern as LoginValidationException (T-008-02).
/// The endpoint catches and maps to VALIDATION (400) via the closed code list (TA-4.1.3); no new code added.
/// </summary>
public sealed class SearchTransfersValidationException : Exception
{
    public SearchTransfersValidationException(string message) : base(message) { }
}
