using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using wa.domain;
using wa.infrastructure.Events;
using Xunit;

namespace wa.application.unit.Events;

/// <summary>CHORE-local-email: the local hand-off publisher posts the exact envelope bytes with an HMAC signature, and never throws.</summary>
public class HttpHandOffEventPublisherTests
{
    private const string Key = "local-email-handoff-key-dev-only";
    private static readonly Uri BaseUrl = new("http://workers.test:8081/");

    private static EventEnvelope NewEnvelope() => new()
    {
        EventId = Guid.NewGuid(),
        Type = EventType.MagicLinkRequested,
        Version = 1,
        OccurredAtUtc = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc),
        CorrelationId = Guid.NewGuid(),
        PartitionKey = "p1",
        Payload = new { email = "a@b.test", purpose = 1 }
    };

    private static HttpHandOffEventPublisher Create(StubHandler handler, CapturingLogger<HttpHandOffEventPublisher> logger) =>
        new(new HttpClient(handler), BaseUrl, Key, logger);

    [Fact]
    public async Task Publish_Ok_PostsExactEnvelopeBytesWithSignature()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var logger = new CapturingLogger<HttpHandOffEventPublisher>();
        using var publisher = Create(handler, logger);
        var envelope = NewEnvelope();

        var record = await publisher.PublishAsync(envelope, CancellationToken.None);

        var expectedBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        Assert.True(record.IsPublished);
        Assert.Equal(envelope.EventId, record.EventId);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("http://workers.test:8081/api/events/email", handler.RequestUri?.ToString());
        Assert.Equal(expectedBytes, handler.Body);
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal(EmailHandOff.Sign(expectedBytes, Key), handler.Signature);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Publish_ServerError_ReturnsUnpublishedAndLogsWarning()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var logger = new CapturingLogger<HttpHandOffEventPublisher>();
        using var publisher = Create(handler, logger);
        var envelope = NewEnvelope();

        var record = await publisher.PublishAsync(envelope, CancellationToken.None);

        Assert.False(record.IsPublished);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.True(logger.Mentions(envelope.EventId.ToString()));
    }

    [Fact]
    public async Task Publish_TransportFailure_ReturnsUnpublishedAndLogsErrorWithoutPayload()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        var logger = new CapturingLogger<HttpHandOffEventPublisher>();
        using var publisher = Create(handler, logger);

        var record = await publisher.PublishAsync(NewEnvelope(), CancellationToken.None);

        Assert.False(record.IsPublished);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.False(logger.Mentions("a@b.test"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public byte[]? Body { get; private set; }
        public string? ContentType { get; private set; }
        public string? Signature { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Signature = request.Headers.TryGetValues(EmailHandOff.HeaderName, out var values) ? values.Single() : null;
            return respond(request, cancellationToken);
        }
    }
}
