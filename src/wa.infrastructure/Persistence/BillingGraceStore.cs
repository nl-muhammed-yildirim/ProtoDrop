using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-019-05 (D-4): adapter for the f-expire billing-grace sub-step. No schema change — Subscription.GraceEndsAtUtc and JobRun already exist.
/// The downgrade is ONE transaction: locked re-read (EC-019-3) → JobRun claim (TA-6.1) → AppUser.PlanId = Free.
/// </summary>
public sealed class BillingGraceStore : IBillingGraceStore
{
    private const string InsertJobRun = "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc], [FinishedAtUtc]) VALUES ({0}, {1}, {2}, {2})";

    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="BillingGraceStore"/> class.</summary>
    /// <param name="factory">Factory that creates a short-lived database context per call.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public BillingGraceStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraceCandidate>> GetExpiredGracesAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var subs = await db.Set<Subscription>().AsNoTracking()
            .Where(s => s.Status == BillingGrace.PausedStatus && s.GraceEndsAtUtc != null && s.GraceEndsAtUtc <= nowUtc)
            .Select(s => new { s.Id, GraceEndsAtUtc = s.GraceEndsAtUtc!.Value })
            .ToListAsync(ct);
        if (subs.Count == 0)
            return [];

        var keys = subs.Select(s => BillingGrace.ClaimJobKey(s.Id)).ToList();
        var runs = await db.Set<JobRun>().AsNoTracking()
            .Where(j => keys.Contains(j.JobKey))
            .Select(j => new { j.JobKey, j.RunAtUtc })
            .ToListAsync(ct);

        var candidates = new List<GraceCandidate>();
        foreach (var sub in subs)
        {
            var key = BillingGrace.ClaimJobKey(sub.Id);
            var anchor = sub.GraceEndsAtUtc;
            if (runs.Any(r => r.JobKey == key && r.RunAtUtc == anchor))
                continue;
            candidates.Add(new GraceCandidate(sub.Id, DateTime.SpecifyKind(anchor, DateTimeKind.Utc)));
        }
        return candidates;
    }

    /// <inheritdoc />
    public async Task<GraceDowngradeResult> TryDowngradeAsync(
        Guid subscriptionId, DateTime graceEndsAtUtc, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // EC-019-3: row lock so a concurrent resume/updated webhook either commits first (we skip) or waits until we commit.
        var subscription = await db.Set<Subscription>()
            .FromSqlRaw("SELECT * FROM [Subscription] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", subscriptionId)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (subscription is null
            || !BillingGrace.IsExpired(subscription.Status, subscription.GraceEndsAtUtc, nowUtc)
            || subscription.GraceEndsAtUtc != graceEndsAtUtc)
        {
            await tx.RollbackAsync(ct);
            return new GraceDowngradeResult(GraceDowngradeOutcome.NotDue);
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                InsertJobRun, [BillingGrace.ClaimJobKey(subscriptionId), graceEndsAtUtc, nowUtc], ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            await tx.RollbackAsync(ct); // double run / scale-out: another instance already claimed this grace
            return new GraceDowngradeResult(GraceDowngradeOutcome.AlreadyClaimed);
        }

        var user = await db.Set<AppUser>().FirstOrDefaultAsync(u => u.Id == subscription.AppUserId && u.DeletedAtUtc == null, ct);
        if (user is null)
        {
            await tx.CommitAsync(ct); // claim kept: a deleted user is never downgraded or emailed
            return new GraceDowngradeResult(GraceDowngradeOutcome.NoChange);
        }

        var free = await db.Set<Plan>().AsNoTracking().FirstAsync(p => p.Code == Plan.DefaultCode, ct);
        if (user.PlanId == free.Id)
        {
            await tx.CommitAsync(ct);
            return new GraceDowngradeResult(GraceDowngradeOutcome.NoChange);
        }

        var fromCode = await db.Set<Plan>().AsNoTracking()
            .Where(p => p.Id == user.PlanId)
            .Select(p => p.Code)
            .FirstAsync(ct);
        user.PlanId = free.Id;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new GraceDowngradeResult(GraceDowngradeOutcome.Downgraded, user.Id, user.Email, fromCode, free.Code);
    }
}
