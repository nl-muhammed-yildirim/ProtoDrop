using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-019-03 (D-3): adapter for IStripeBillingStore. A fresh WaDbContext per call (a context is not safe
/// for concurrent webhook deliveries to share); dedup and mirror upsert share ONE transaction.
/// </summary>
public sealed class StripeBillingStore : IStripeBillingStore
{
    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="StripeBillingStore"/> class.</summary>
    /// <param name="factory">Factory that creates a short-lived database context per call.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public StripeBillingStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public async Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var plans = await db.Set<Plan>().AsNoTracking().ToListAsync(ct);
        foreach (var plan in plans)
        {
            var price = PlanPrice.Read(plan);
            if (string.Equals(price.Monthly?.StripePriceId, priceId, StringComparison.Ordinal)
                || string.Equals(price.Annual?.StripePriceId, priceId, StringComparison.Ordinal))
                return plan;
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<Subscription>().AsNoTracking()
            .Where(s => s.AppUserId == userId && s.StripeCustomerId != null)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Select(s => s.StripeCustomerId)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<StripeApplyResult> ApplySubscriptionAsync(
        StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            return await ApplyOnceAsync(change, targetPlanId, nowUtc, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two deliveries for a brand-new subscription (created + updated) both saw no row and both inserted; the loser hit
            // UQ_Sub_Stripe and its transaction (incl. its StripeEvent row) was rolled back. Retry ONCE: the winner's row is now
            // committed, so the re-read under the lock takes the update path. A second violation propagates → 500 → Stripe redelivers.
            return await ApplyOnceAsync(change, targetPlanId, nowUtc, ct);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private async Task<StripeApplyResult> ApplyOnceAsync(
        StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // Raw INSERT bypasses the change tracker; a PK collision means another delivery already owns this event id.
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [StripeEvent] ([StripeEventId], [ProcessedAtUtc]) VALUES ({0}, {1})",
                [change.StripeEventId, nowUtc], ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            await tx.RollbackAsync(ct);
            return StripeApplyResult.Of(StripeApplyOutcome.Duplicate);
        }

        // US-019-06 (D-3, EC-019-3): same UPDLOCK the grace timer takes, so a resume racing the timer is serialized.
        // HOLDLOCK adds a key-range lock on UQ_Sub_Stripe so a MISSING row is locked too (concurrent created + updated for a new subscription).
        var subscription = await db.Set<Subscription>()
            .FromSqlRaw("SELECT * FROM [Subscription] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [StripeSubId] = {0}", change.StripeSubId)
            .FirstOrDefaultAsync(ct);
        var previousStatus = subscription?.Status; // null for a new row (US-019-05 grace transition)
        var userId = subscription?.AppUserId ?? change.MetadataUserId;
        var user = userId is null
            ? null
            : await db.Set<AppUser>().FirstOrDefaultAsync(u => u.Id == userId.Value && u.DeletedAtUtc == null, ct);
        if (user is null)
        {
            await tx.RollbackAsync(ct); // StripeEvent row not kept → Stripe's redelivery reprocesses (EC-019-1)
            return StripeApplyResult.Of(StripeApplyOutcome.UserUnresolved);
        }

        var fromPlan = await db.Set<Plan>().AsNoTracking().FirstAsync(p => p.Id == user.PlanId, ct);
        var toPlan = targetPlanId is { } target && target != user.PlanId
            ? await db.Set<Plan>().AsNoTracking().FirstAsync(p => p.Id == target, ct)
            : fromPlan;

        if (subscription is null)
        {
            subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                StripeSubId = change.StripeSubId,
                CreatedAtUtc = nowUtc
            };
            db.Set<Subscription>().Add(subscription);
        }

        subscription.StripeCustomerId = change.StripeCustomerId;
        subscription.PlanCode = targetPlanId.HasValue ? toPlan.Code : (subscription.PlanCode ?? fromPlan.Code);
        subscription.Status = change.Status;
        subscription.CurrentPeriodEndUtc = change.CurrentPeriodEndUtc;
        subscription.UpdatedAtUtc = nowUtc;
        subscription.GraceEndsAtUtc = BillingGrace.NextGraceEndsAt(previousStatus, subscription.GraceEndsAtUtc, change.Status, nowUtc);

        if (targetPlanId.HasValue)
            user.PlanId = toPlan.Id;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new StripeApplyResult(StripeApplyOutcome.Applied, user.Id, fromPlan.Id, toPlan.Id, fromPlan.Code, toPlan.Code);
    }
}
