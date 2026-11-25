using Microsoft.EntityFrameworkCore;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-003-10: after a raw-SQL UPDATE on a Transfer row, drop any stale tracked copy from the singleton context.
/// NEVER Local.Remove — on a tracked entity it marks it Deleted (-> "association severed" / DELETE on the next SaveChanges).
/// </summary>
internal static class TransferTrackerExtensions
{
    /// <summary>Detaches the transfer.</summary>
    /// <param name="db">The database.</param>
    /// <param name="transferId">Identifier of the transfer.</param>
    public static void DetachTransfer(this WaDbContext db, Guid transferId)
    {
        // ChangeTracker.Entries (not Local): Local hides Deleted entries, so this also cleans one that is already Deleted.
        foreach (var entry in db.ChangeTracker.Entries<Transfer>().Where(e => e.Entity.Id == transferId).ToList())
            entry.State = EntityState.Detached;
    }
}
