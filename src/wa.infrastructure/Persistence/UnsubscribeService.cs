using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using wa.domain;
using wa.application.UseCases.Transfers;

namespace wa.infrastructure.Persistence;

/// <summary>T-006-03 (FR-006-5): Unsubscribe result — Success or NotFound.</summary>
public record UnsubscribeResult(bool Success);

/// <summary>
/// T-006-03 (FR-006-5, TA-6.7): Unsubscribe handler — verify token + insert EmailSuppression idempotently.
/// Plain sealed class (no MediatR) following the T-006-01 pattern; called directly from Program.cs's MapGet.
/// </summary>
public sealed class UnsubscribeService
{
    private readonly WaDbContext _db;
    private readonly byte[] _hmacKey;
    private readonly ILogger<UnsubscribeService> _logger;

    public UnsubscribeService(WaDbContext db, byte[] hmacKey, ILogger<UnsubscribeService> logger)
    {
        _db = db;
        _hmacKey = hmacKey;
        _logger = logger;
    }

    /// <summary>
    /// Verify the token and insert an EmailSuppression row (idempotent via UQ_ES unique constraint).
    /// EC-006-3: suppression created mid-batch can at most save or miss one email — documented, no lock needed.
    /// </summary>
    public async Task<UnsubscribeResult> HandleAsync(string token, CancellationToken ct)
    {
        if (UnsubscribeToken.Verify(token, _hmacKey) is not (string Address, string SenderEmail))
        {
            // TA-9.6: tampered/unknown → 404 (don't leak "token invalid" detail). Warn telemetry for admin alerting.
            _logger.LogWarning("Unsubscribe token verification failed — returning 404");
            return new UnsubscribeResult(false);
        }

        var address = Address;
        var senderEmail = SenderEmail;

        try
        {
            _db.Set<EmailSuppression>().Add(new EmailSuppression
            {
                Address = address,
                SenderEmail = senderEmail,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return new UnsubscribeResult(true);
        }
        catch (DbUpdateException)
        {
            // UQ_ES unique constraint violation — the row already exists (double-click / stale token).
            // Idempotent: treat as success. EC-006-3 documented.
            _logger.LogInformation("Unsubscribe idempotent no-op for {Address} + {SenderEmail}", address, senderEmail);
            return new UnsubscribeResult(true);
        }
    }
}
