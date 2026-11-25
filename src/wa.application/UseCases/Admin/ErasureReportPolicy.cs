using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>US-047-03: page size is the TA-4.1.4 API pin (not a plan limit), as GdprSweepPolicy is for the sweep.</summary>
public static class ErasureReportPolicy
{
    public const int PageSize = 25;
    public const string StatusPending = "pending";
    public const string StatusSwept = "swept";
}
