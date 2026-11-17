using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-05 (D-3): shared contract for f-email handlers so EmailFunction's dispatch switch has a common type (fixes CS8506).
/// Lives in wa.application so all handlers stay Service-Bus-free (the workers project owns the SB trigger).
/// </summary>
public interface IEmailEventHandler
{
    Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct);
}
