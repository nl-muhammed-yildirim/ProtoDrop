namespace wa.application.UseCases.Transfers;

/// <summary>US-022-01 (FR-022-3): the sender's plan (or the global feature.scheduling gate) does not allow scheduling.
/// The endpoint maps it to 403 FORBIDDEN, keeping the closed code list intact.</summary>
public sealed class ScheduleForbiddenException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScheduleForbiddenException"/> class with the standard message.</summary>
    public ScheduleForbiddenException() : base("Pro plan required.") { }
}
