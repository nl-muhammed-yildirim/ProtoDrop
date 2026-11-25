using wa.application.Limits;
using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>US-023-01 (FR-023-5): the viewer's plan (or the global feature.analytics gate) does not include analytics.
/// The endpoint maps it to 403 FORBIDDEN, keeping the closed code list intact.</summary>
public sealed class AnalyticsForbiddenException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AnalyticsForbiddenException"/> class.</summary>
    public AnalyticsForbiddenException() : base("Pro plan required.") { }
}
