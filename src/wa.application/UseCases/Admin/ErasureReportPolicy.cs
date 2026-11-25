using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>US-047-03: page size is the TA-4.1.4 API pin (not a plan limit), as GdprSweepPolicy is for the sweep.</summary>
public static class ErasureReportPolicy
{
    /// <summary>The page size constant; value <c>25</c>.</summary>
    public const int PageSize = 25;
    /// <summary>The status pending constant; value <c>pending</c>.</summary>
    public const string StatusPending = "pending";
    /// <summary>The status swept constant; value <c>swept</c>.</summary>
    public const string StatusSwept = "swept";
}
