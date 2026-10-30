using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-05 (D-3): self-contained f-email handler for forgot-password auth.magic_link_requested events
/// (the purpose=2 payload discriminator routes here — see EmailFunction.IsForgotPasswordPayload).
/// Same shape as SendMagicLinkEmailHandler, three diffs: subject "Your ProtoDrop link", template dir
/// Emails/templates/forgot-password/, kind="forgot_password" in email.sent/email.failed payloads.
/// Self-contained: no DB context needed — payload carries email + token + expiry.
/// </summary>
public sealed class SendForgotPasswordEmailHandler : IEmailEventHandler
{
    private const string Subject = "Your ProtoDrop link"; // FR-008-6: the forgot-password subject (magic-link's is "Your ProtoDrop sign-in link")

    private readonly IEmailSender _emailSender;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfiguration _config;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<SendForgotPasswordEmailHandler> _logger;
    private readonly (string Html, string Txt) _templates;

    public SendForgotPasswordEmailHandler(
        IEmailSender emailSender,
        IEventPublisher eventPublisher,
        IConfiguration config,
        WaTelemetryReporter telemetry,
        ILogger<SendForgotPasswordEmailHandler> logger)
    {
        _emailSender = emailSender;
        _eventPublisher = eventPublisher;
        _config = config;
        _telemetry = telemetry;
        _logger = logger;

        var basePath = Path.Combine(AppContext.BaseDirectory, "Emails", "templates", "forgot-password");
        var htmlPath = Path.Combine(basePath, "en.html");
        var txtPath = Path.Combine(basePath, "en.txt");

        if (File.Exists(htmlPath) && File.Exists(txtPath))
            _templates = (File.ReadAllText(htmlPath), File.ReadAllText(txtPath));
        else
            throw new InvalidOperationException(
                $"Forgot-password email templates not found. Expected: {htmlPath} and {txtPath}. " +
                "Verify the workers csproj Content Include for Emails\\templates\\**\\*.");
    }

    public async Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)
    {
        // 1. Extract email, raw token, and expiry from the payload (TA-9.1: only hash is persisted).
        // Read-by-name parsing — the extra purpose field (D-3 discriminator) is ignored (unknown-field safety).
        var payloadJson = JsonSerializer.Serialize(envelope.Payload);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var root = payloadDoc.RootElement;

        if (!root.TryGetProperty("email", out var emailEl) ||
            string.IsNullOrWhiteSpace(emailEl.GetString()))
        {
            _logger.LogWarning("f-email: missing or invalid email in forgot-password payload");
            return;
        }

        if (!root.TryGetProperty("token", out var tokenEl) ||
            string.IsNullOrWhiteSpace(tokenEl.GetString()))
        {
            _logger.LogWarning("f-email: missing or invalid token in forgot-password payload");
            return;
        }

        var email = emailEl.GetString()!;
        var rawToken = tokenEl.GetString()!;
        DateTime expiresAtUtc;
        if (root.TryGetProperty("expiresAtUtc", out var expiryEl) && expiryEl.TryGetDateTime(out var expiry))
            expiresAtUtc = expiry;
        else
            expiresAtUtc = DateTime.UtcNow.AddMinutes(10); // fallback

        // 2. Build CTA URL: {publicOrigin}/auth?ml={rawToken} — same as magic-link (redeem endpoint 10 is purpose-agnostic).
        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/auth?ml={rawToken}";

        // 3. Compute remaining minutes for the expiry note.
        var minutesRemaining = (int)Math.Ceiling((expiresAtUtc - DateTime.UtcNow).TotalMinutes);

        // 4. Render templates with manual variable substitution.
        var htmlBody = RenderTemplate(_templates.Html, email, ctaUrl, minutesRemaining);
        var textBody = RenderTemplate(_templates.Txt, email, ctaUrl, minutesRemaining);

        try
        {
            // 5. Send the email.
            var fromAddress = _config["Wa:Email:FromAddress"] ?? "no-reply@protodrop.com";
            var emailMessage = new EmailMessage(
                email,
                Subject,
                htmlBody,
                textBody,
                fromAddress,
                null);

            await _emailSender.SendAsync(emailMessage, ct);

            // 6. Emit email.sent event (same pattern as SendMagicLinkEmailHandler).
            var emailSentEnvelope = new EventEnvelope
            {
                EventId = Guid.NewGuid(),
                Type = EventType.EmailSent,
                Version = 1,
                OccurredAtUtc = DateTime.UtcNow,
                CorrelationId = envelope.CorrelationId,
                PartitionKey = email,
                Payload = new
                {
                    to = email,
                    attempt,
                    kind = "forgot_password"
                }
            };
            await _eventPublisher.PublishAsync(emailSentEnvelope, ct);

            // Telemetry.
            _telemetry.LogEvent(TelemetryEvents.EmailSent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-email: failed to send forgot-password email attempt {Attempt}", attempt);

            if (attempt == maxDeliveryCount)
            {
                // Final failure (TA-5.1): broker dead-letters after this throw — emit the observables first.
                var reason = ex.GetType().Name;
                var emailFailedEnvelope = new EventEnvelope
                {
                    EventId = Guid.NewGuid(),
                    Type = EventType.EmailFailed,
                    Version = 1,
                    OccurredAtUtc = DateTime.UtcNow,
                    CorrelationId = envelope.CorrelationId,
                    PartitionKey = email,
                    Payload = new
                    {
                        to = email,
                        attempt = maxDeliveryCount,
                        reason,
                        kind = "forgot_password"
                    }
                };
                await _eventPublisher.PublishAsync(emailFailedEnvelope, ct);
                _telemetry.LogEvent(TelemetryEvents.DlqCount);
                _telemetry.EmailFailures1h();
            }

            throw; // transient → redeliver; on last attempt the broker dead-letters (TA-5.1)
        }
    }

    /// <summary>Render a Stubble-style template using manual {{Variable}} substitution.</summary>
    private static string RenderTemplate(string template, string email, string ctaUrl, int minutesRemaining) =>
        template
            .Replace("{{Email}}", email)
            .Replace("{{CtaUrl}}", ctaUrl)
            .Replace("{{MinutesRemaining}}", minutesRemaining.ToString());
}
