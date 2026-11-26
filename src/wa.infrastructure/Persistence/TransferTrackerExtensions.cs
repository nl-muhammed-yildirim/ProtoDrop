using Microsoft.EntityFrameworkCore;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-003-10: after a raw-SQL UPDATE on a Transfer row, drop any stale tracked copy from the call's context (a no-op on a fresh context, still guards within one call).
/// NEVER Local.Remove — on a tracked entity it marks it Deleted (-> "association severed" / DELETE on the next SaveChanges).
/// </summary>
internal static class TransferTrackerExtensions
{
    /// <summary>Removes every tracked entry of one transfer from the context's change tracker so that later reads see the row as the raw SQL left it.</summary>
    /// <param name="db">Context whose change tracker is cleaned.</param>
    /// <param name="transferId">Id of the transfer to detach.</param>
    public static void DetachTransfer(this WaDbContext db, Guid transferId)
    {
        // ChangeTracker.Entries (not Local): Local hides Deleted entries, so this also cleans one that is already Deleted.
        foreach (var entry in db.ChangeTracker.Entries<Transfer>().Where(e => e.Entity.Id == transferId).ToList())
            entry.State = EntityState.Detached;
    }
}
