using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-010-02 (AC-010-2/EC-010-2): a re-send's shared blobs are physically gone — thrown by SendStore's gate at
/// creation time (SaveResendAsync step 0) or send time (SaveSendAsync, SourcedFrom drafts only). The endpoints catch
/// it and throw BusinessException(ProblemCodes.FilesGone), keeping the closed code list intact (410 FILES_GONE).
/// </summary>
public sealed class FilesGoneException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="FilesGoneException"/> class with the standard "upload again" message.</summary>
    public FilesGoneException() : base("The files for this transfer were deleted — upload again.") { }
}
