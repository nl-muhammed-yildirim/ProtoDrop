using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/public/transfers/{linkId}/unlock request (TA-4.2#5). Null Password = missing body/field → the handler throws the VALIDATION application-level exception (D1).</summary>
public sealed record UnlockTransferCommand(string LinkId, string? Password) : IRequest<UnlockTransferResult>;

/// <summary>The unlock endpoint's state mapping — mirrors endpoint 4's PublicTransferState terminal states (TA-9.6 enumeration: same bodies) + WrongPassword for the 401.</summary>
public enum UnlockState { Active, Expired, DownloadLimit, NotFound, WrongPassword }

/// <summary>Result of UnlockTransferCommand — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a). Token only on Active; SenderEmail only on terminal states (renders into Problem+JSON details, FR-003-8/9).</summary>
public sealed record UnlockTransferResult(
    UnlockState State,
    string? Token = null,           // Active only — the 7-day HS256 JWT (TA-4.2#5)
    string? SenderEmail = null);    // Expired/DownloadLimit only

/// <summary>
/// T-003-04 (TA-4.2#5, TA-9.1): the only business logic behind POST /api/v1/public/transfers/{linkId}/unlock:
/// PBKDF2-SHA256 verify against Transfer.PasswordHash with the SAME PasswordHasher + user-id keying as SendTransfer (transfer.Id.ToString("n"));
/// correct → mint the 7-day unlock JWT. Status always wins over credentials (D6/US-003-05) — terminal rows never reach the password logic.
/// </summary>
public sealed class UnlockTransferHandler : IRequestHandler<UnlockTransferCommand, UnlockTransferResult>
{
    private readonly IPublicTransferStore _store;
    private readonly IUnlockTokenMinter _minter;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<UnlockTransferHandler> _logger;

    public UnlockTransferHandler(
        IPublicTransferStore store,
        IUnlockTokenMinter minter,
        WaTelemetryReporter telemetry,
        ILogger<UnlockTransferHandler> logger)
    {
        _store = store;
        _minter = minter;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<UnlockTransferResult> Handle(UnlockTransferCommand request, CancellationToken cancellationToken)
    {
        // D1: null password (missing body/field) → VALIDATION via the application-level exception, BEFORE any DB access.
        if (request.Password is null)
            throw new UnlockPasswordMissingException("A password is required to unlock this transfer.");

        // Same post-query filter as endpoint 4 (TA-9.6): malformed linkIds are treated as unknown — no format-error enumeration.
        var (transfer, _) = await _store.GetByLinkIdAsync(request.LinkId, cancellationToken);
        if (transfer is not null && !IsWellFormedLinkId(request.LinkId))
            transfer = null;

        // D6: status check BEFORE any password logic — an expired/deleted row cannot be unlocked regardless of credentials.
        if (transfer is null || transfer.Status == 4)
            return new UnlockTransferResult(UnlockState.NotFound);

        if (transfer.Status == 2)
            return new UnlockTransferResult(UnlockState.Expired, SenderEmail: transfer.SenderEmail);

        if (transfer.Status == 3)
            return new UnlockTransferResult(UnlockState.DownloadLimit, SenderEmail: transfer.SenderEmail);

        // Active but no password set → NotFound (FR-003-9 single-body invariant — same as endpoint 4's unknown case).
        if (transfer.PasswordHash is null)
            return new UnlockTransferResult(UnlockState.NotFound);

        // TA-9.1: exact inverse of SendTransfer.cs — same PasswordHasher<string>, same user-id keying (transfer.Id.ToString("n")).
        // Any drift here means every correct password fails; the integration + unit tests pin this via a known id+hash pair.
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<string>();
        var verifyStatus = hasher.VerifyHashedPassword(transfer.Id.ToString("n"), transfer.PasswordHash, request.Password);

        if (verifyStatus is not (Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success or Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded))
        {
            // TA-9.4: property-free event — the password value never enters any log line; only the closed name travels.
            _telemetry.LogEvent(TelemetryEvents.PasswordWrong);
            return new UnlockTransferResult(UnlockState.WrongPassword); // D2.6: result state, not an exception — Program.cs maps the 401 body
        }

        _telemetry.LogEvent(TelemetryEvents.PasswordCorrect);
        return new UnlockTransferResult(UnlockState.Active, Token: _minter.Mint(request.LinkId));
    }

    /// <summary>8 chars from the LinkId charset (TA-9.6) — same filter as GetPublicTransferHandler's private helper.</summary>
    private static bool IsWellFormedLinkId(string linkId) =>
        linkId.Length == LinkId.Length && Enumerable.All(linkId, c => LinkId.Charset.Contains(c));
}

/// <summary>
/// T-003-04 (D1): the password field is missing from the unlock request body. The endpoint catches this and throws
/// BusinessException(ProblemCodes.Validation) with errors[] — keeps the closed code list intact (same precedent as TransferValidationException).
/// </summary>
public sealed class UnlockPasswordMissingException : Exception
{
    public UnlockPasswordMissingException(string message) : base(message) { }
}
