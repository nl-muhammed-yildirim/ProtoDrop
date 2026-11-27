using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Events;

/// <summary>
/// CHORE-local-email: <see cref="IEventPublisher"/> for the local Docker stack. Posts every envelope to the workers'
/// <c>f-email-handoff</c> trigger, signed with an HMAC over the exact body bytes, so local mail flows without Service Bus.
/// Fire-and-log: a failed hand-off is logged (ids and wire name only, never the payload) and reported as unpublished, never thrown.
/// Selected by the API only in Local/Development (TA-0.2 rule 7); production keeps <see cref="SbEventPublisher"/>.
/// </summary>
/// <param name="httpClient">Client used for the POST; owned by this publisher.</param>
/// <param name="baseUrl">Workers base URL, ending in a slash.</param>
/// <param name="signKey">Shared key that signs the body.</param>
/// <param name="logger">Logger for hand-off failures.</param>
public sealed partial class HttpHandOffEventPublisher(HttpClient httpClient, Uri baseUrl, string signKey, ILogger<HttpHandOffEventPublisher> logger)
    : IEventPublisher, IDisposable
{
    /// <summary>Hand-off timeout (transport timeout, mirrors <see cref="SbEventPublisher"/>; TA-4.1.7).</summary>
    private static readonly TimeSpan HandOffTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient = httpClient;
    private readonly Uri _baseUrl = baseUrl;
    private readonly string _signKey = signKey;
    private readonly ILogger<HttpHandOffEventPublisher> _logger = logger;

    /// <inheritdoc />
    public async Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var wireName = EventTypeConverter.WireName(envelope.Type);
        var published = false;
        try
        {
            // Serialize once: the signed bytes are exactly the bytes on the wire (same shape as SbEventPublisher / InMemoryEventPublisher).
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, EmailHandOff.RelativeUrl))
            {
                Content = new ByteArrayContent(body)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation(EmailHandOff.HeaderName, EmailHandOff.Sign(body, _signKey));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandOffTimeout);
            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
                published = true;
            else
                LogHandOffRejected(_logger, envelope.EventId, wireName, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            LogHandOffFailed(_logger, ex, envelope.EventId, wireName);
        }

        return new DomainEventRecord { EventId = envelope.EventId, IsPublished = published };
    }

    /// <summary>Disposes the owned HTTP client.</summary>
    public void Dispose() => _httpClient.Dispose();

    [LoggerMessage(Level = LogLevel.Error, Message = "Email hand-off failed for event {EventId} type {EventType}")]
    private static partial void LogHandOffFailed(ILogger logger, Exception exception, Guid eventId, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email hand-off rejected for event {EventId} type {EventType} status={StatusCode}")]
    private static partial void LogHandOffRejected(ILogger logger, Guid eventId, string eventType, int statusCode);
}
