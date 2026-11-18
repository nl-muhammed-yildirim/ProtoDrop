using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-019-04 (D-1, D-5): ledger adapter for the payment-failed email sequence. No schema change — StripeEvent rows dedup
/// per event ("evt_…") and per invoice ("pf:in_…"); one JobRun row per step (TA-6.1 claim) records progress and cancellation.
/// A fresh WaDbContext per call (used by both the API and the workers).
/// </summary>
public sealed class BillingSequenceStore : IBillingSequenceStore
{
    private const string InsertStripeEvent = "INSERT INTO [StripeEvent] ([StripeEventId], [ProcessedAtUtc]) VALUES ({0}, {1})";
    private const string InsertJobRun = "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc], [FinishedAtUtc]) VALUES ({0}, {1}, {2}, {2})";

    private readonly IDbContextFactory<WaDbContext> _factory;

    public BillingSequenceStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<PaymentFailedResult> StartPaymentFailedAsync(
        string eventId, string invoiceId, string stripeSubId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await TryInsertAsync(db, InsertStripeEvent, [eventId, nowUtc], ct))
        {
            await tx.RollbackAsync(ct);
            return new PaymentFailedResult(PaymentFailedOutcome.DuplicateEvent);
        }

        var subscription = await db.Set<Subscription>().FirstOrDefaultAsync(s => s.StripeSubId == stripeSubId, ct);
        if (subscription is null)
        {
            await tx.RollbackAsync(ct); // event row not kept → Stripe's redelivery reprocesses (EC-019-1)
            return new PaymentFailedResult(PaymentFailedOutcome.SubscriptionUnresolved);
        }

        if (!BillingEmailSchedule.IsEligible(subscription.Status))
        {
            await tx.CommitAsync(ct); // EC-019-2: event row kept, nothing else changes
            return new PaymentFailedResult(PaymentFailedOutcome.Ineligible);
        }

        var invoiceKey = BillingEmailSchedule.InvoiceDedupKey(invoiceId);
        if (await db.Set<StripeEvent>().AnyAsync(e => e.StripeEventId == invoiceKey, ct))
        {
            await tx.CommitAsync(ct);
            return new PaymentFailedResult(PaymentFailedOutcome.DuplicateInvoice);
        }
        if (!await TryInsertAsync(db, InsertStripeEvent, [invoiceKey, nowUtc], ct))
        {
            await tx.RollbackAsync(ct); // concurrent twin won the race
            return new PaymentFailedResult(PaymentFailedOutcome.DuplicateInvoice);
        }

        subscription.Status = "past_due";
        subscription.UpdatedAtUtc = nowUtc;
        db.Set<JobRun>().Add(new JobRun
        {
            JobKey = BillingEmailSchedule.StepJobKey(subscription.Id, 1),
            RunAtUtc = nowUtc,
            LockUntilUtc = nowUtc,
            FinishedAtUtc = nowUtc
        });

        var email = await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == subscription.AppUserId && u.DeletedAtUtc == null)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new PaymentFailedResult(PaymentFailedOutcome.Started, subscription.Id, subscription.AppUserId, email);
    }

    public async Task<InvoicePaidOutcome> CancelSequenceAsync(
        string eventId, string stripeSubId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await TryInsertAsync(db, InsertStripeEvent, [eventId, nowUtc], ct))
        {
            await tx.RollbackAsync(ct);
            return InvoicePaidOutcome.DuplicateEvent;
        }

        var subscription = await db.Set<Subscription>().FirstOrDefaultAsync(s => s.StripeSubId == stripeSubId, ct);
        if (subscription is null)
        {
            await tx.CommitAsync(ct);
            return InvoicePaidOutcome.NoSequence;
        }

        if (subscription.Status == "past_due")
        {
            subscription.Status = "active"; // story: "active restored"
            subscription.UpdatedAtUtc = nowUtc;
        }

        var step1Key = BillingEmailSchedule.StepJobKey(subscription.Id, 1);
        var anchor = await db.Set<JobRun>().AsNoTracking()
            .Where(j => j.JobKey == step1Key)
            .OrderByDescending(j => j.RunAtUtc)
            .Select(j => (DateTime?)j.RunAtUtc)
            .FirstOrDefaultAsync(ct);
        if (anchor is null)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return InvoicePaidOutcome.NoSequence;
        }

        for (var step = 2; step <= BillingEmailSchedule.StepCount; step++)
        {
            var key = BillingEmailSchedule.StepJobKey(subscription.Id, step);
            var runAt = anchor.Value;
            var exists = await db.Set<JobRun>().AnyAsync(j => j.JobKey == key && j.RunAtUtc == runAt, ct);
            if (exists)
                continue;
            if (!await TryInsertAsync(db, InsertJobRun, [key, runAt, nowUtc], ct))
            {
                await tx.RollbackAsync(ct); // the pass claimed it at the same moment
                return InvoicePaidOutcome.Conflict;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return InvoicePaidOutcome.Cancelled;
    }

    public async Task<IReadOnlyList<BillingSequenceRow>> GetOpenSequencesAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var subs = await db.Set<Subscription>().AsNoTracking()
            .Where(s => s.Status == "past_due")
            .Select(s => new { s.Id, s.AppUserId })
            .ToListAsync(ct);
        if (subs.Count == 0)
            return [];

        var userIds = subs.Select(s => s.AppUserId).Distinct().ToList();
        var emails = await db.Set<AppUser>().AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.DeletedAtUtc == null)
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        var keys = subs
            .SelectMany(s => Enumerable.Range(1, BillingEmailSchedule.StepCount).Select(n => BillingEmailSchedule.StepJobKey(s.Id, n)))
            .ToList();
        var runs = await db.Set<JobRun>().AsNoTracking()
            .Where(j => keys.Contains(j.JobKey))
            .Select(j => new { j.JobKey, j.RunAtUtc })
            .ToListAsync(ct);

        var rows = new List<BillingSequenceRow>();
        foreach (var sub in subs)
        {
            if (!emails.TryGetValue(sub.AppUserId, out var email))
                continue;

            var step1Key = BillingEmailSchedule.StepJobKey(sub.Id, 1);
            var step1Runs = runs.Where(r => r.JobKey == step1Key).ToList();
            if (step1Runs.Count == 0)
                continue;

            var anchor = step1Runs.Max(r => r.RunAtUtc);
            var claimed = new List<int>();
            for (var step = 1; step <= BillingEmailSchedule.StepCount; step++)
            {
                var key = BillingEmailSchedule.StepJobKey(sub.Id, step);
                if (runs.Any(r => r.JobKey == key && r.RunAtUtc == anchor))
                    claimed.Add(step);
            }
            rows.Add(new BillingSequenceRow(sub.Id, sub.AppUserId, email, DateTime.SpecifyKind(anchor, DateTimeKind.Utc), claimed));
        }
        return rows;
    }

    public async Task<bool> TryClaimStepAsync(Guid subscriptionId, int step, DateTime anchorUtc, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await TryInsertAsync(db, InsertJobRun, [BillingEmailSchedule.StepJobKey(subscriptionId, step), anchorUtc, nowUtc], ct);
    }

    /// <summary>Raw INSERT (bypasses the change tracker); false on a PK collision (2627 / 2601) = already claimed.</summary>
    private static async Task<bool> TryInsertAsync(WaDbContext db, string sql, object[] parameters, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            return false;
        }
    }
}
