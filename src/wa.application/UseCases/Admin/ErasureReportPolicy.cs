using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>US-047-03: page size is the TA-4.1.4 API pin (not a plan limit), as GdprSweepPolicy is for the sweep.</summary>
public static class ErasureReportPolicy
{
    /// <summary>Number of erasure rows per page of the admin report.</summary>
    public const int PageSize = 25;
    /// <summary>Wire value of <c>sweepStatus</c> for an erasure the sweep has not finished yet.</summary>
    public const string StatusPending = "pending";
    /// <summary>Wire value of <c>sweepStatus</c> for an erasure the sweep has completed.</summary>
    public const string StatusSwept = "swept";
}
