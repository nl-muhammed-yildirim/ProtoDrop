using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using wa.domain;
using wa.infrastructure.Events;
using Xunit;

namespace wa.workers.unit.Email;

/// <summary>
/// CHORE-local-email: the f-email-handoff HTTP trigger authenticates the API's signed POST and the f-email Service Bus trigger
/// delegates to the same <see cref="IEmailEnvelopeDispatcher"/>, so both paths share one handling code path.
/// </summary>
public class EmailTriggersTests
{
    private const string Key = "local-email-handoff-key-dev-only";
    private const string Json = "{\"eventId\":\"6f1c1f0e-0000-4000-8000-000000000001\"}";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static IConfiguration DevConfig(string? key = Key) =>
        Config(("AZURE_FUNCTIONS_ENVIRONMENT", "Development"), ("Wa:Email:HandOffSignKey", key));

    private static EmailHandOffFunction Build(RecordingDispatcher dispatcher, IConfiguration config) =>
        new(dispatcher, config);

    private static string Sign(string body) => EmailHandOff.Sign(Encoding.UTF8.GetBytes(body), Key);

    private static async Task<string> ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        return await new StreamReader(response.Body).ReadToEndAsync();
    }

    private static async Task<string?> CodeOf(HttpResponseData response) =>
        JsonDocument.Parse(await ReadBody(response)).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task HandOff_MissingHeader_Is401Unauthenticated()
    {
        var dispatcher = new RecordingDispatcher();

        var response = await Build(dispatcher, DevConfig()).Run(new StubFunctionContext(), new StubHttpRequest(Json));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHENTICATED", await CodeOf(response));
        Assert.Empty(dispatcher.Calls);
    }

    [Fact]
    public async Task HandOff_WrongSignature_Is401()
    {
        var dispatcher = new RecordingDispatcher();

        var response = await Build(dispatcher, DevConfig()).Run(new StubFunctionContext(), new StubHttpRequest(Json, Sign("{\"other\":1}")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(dispatcher.Calls);
    }

    [Fact]
    public async Task HandOff_NoKeyConfigured_Is401()
    {
        var dispatcher = new RecordingDispatcher();

        var response = await Build(dispatcher, DevConfig(key: null)).Run(new StubFunctionContext(), new StubHttpRequest(Json, Sign(Json)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(dispatcher.Calls);
    }

    [Fact]
    public async Task HandOff_ValidSignatureInProduction_Is404()
    {
        var dispatcher = new RecordingDispatcher();
        var config = Config(("AZURE_FUNCTIONS_ENVIRONMENT", "Production"), ("Wa:Email:HandOffSignKey", Key));

        var response = await Build(dispatcher, config).Run(new StubFunctionContext(), new StubHttpRequest(Json, Sign(Json)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(dispatcher.Calls);
    }

    [Fact]
    public async Task HandOff_ValidSignature_DispatchesExactBodyAsFinalAttempt()
    {
        var dispatcher = new RecordingDispatcher();

        var response = await Build(dispatcher, DevConfig()).Run(new StubFunctionContext(), new StubHttpRequest(Json, Sign(Json)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(dispatcher.Calls);
        Assert.Equal(Json, call.Json);
        Assert.Equal(EmailEnvelopeDispatcher.MaxDeliveryCount, call.DeliveryCount);
    }

    [Fact]
    public async Task HandOff_DispatcherThrows_Is500Internal()
    {
        var dispatcher = new RecordingDispatcher { Failure = new InvalidOperationException("boom") };

        var response = await Build(dispatcher, DevConfig()).Run(new StubFunctionContext(), new StubHttpRequest(Json, Sign(Json)));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL", await CodeOf(response));
    }

    [Fact]
    public async Task ServiceBusTrigger_DelegatesToTheSameDispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString(Json), deliveryCount: 3);

        await new EmailFunction(dispatcher).Run(new StubFunctionContext(), message);

        var call = Assert.Single(dispatcher.Calls);
        Assert.Equal(Json, call.Json);
        Assert.Equal(3, call.DeliveryCount);
    }

    [Fact]
    public async Task Dispatcher_UnroutedEventType_AndNullEnvelope_CompleteWithoutHandlers()
    {
        var dispatcher = new EmailEnvelopeDispatcher(null!, null!, null!, null!, NullLogger<EmailEnvelopeDispatcher>.Instance);
        var emailSent = JsonSerializer.Serialize(new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.EmailSent,
            OccurredAtUtc = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc),
            CorrelationId = Guid.NewGuid(),
            PartitionKey = "p",
            Payload = new { }
        });

        await dispatcher.DispatchAsync(emailSent, 1, CancellationToken.None);
        await dispatcher.DispatchAsync("null", 1, CancellationToken.None);
    }

    // ---- Hand stubs (no Moq) ----

    private sealed class RecordingDispatcher : IEmailEnvelopeDispatcher
    {
        public List<(string Json, int DeliveryCount)> Calls { get; } = [];
        public Exception? Failure { get; init; }

        public Task DispatchAsync(string envelopeJson, int deliveryCount, CancellationToken ct)
        {
            Calls.Add((envelopeJson, deliveryCount));
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class StubHttpRequest : HttpRequestData
    {
        private readonly HttpHeadersCollection _headers = new();
        private readonly byte[] _body;

        public StubHttpRequest(string body, string? signature = null)
            : base(new StubFunctionContext())
        {
            _body = Encoding.UTF8.GetBytes(body);
            if (signature is not null)
                _headers.Add(EmailHandOff.HeaderName, signature);
        }

        public override Stream Body => new MemoryStream(_body);
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override HttpHeadersCollection Headers => _headers;
        public override IEnumerable<ClaimsIdentity> Identities => Enumerable.Empty<ClaimsIdentity>();
        public override string Method => "POST";
        public override Uri Url => new("http://localhost:8081/api/events/email");

        public override HttpResponseData CreateResponse() => new StubHttpResponse();
    }

    private sealed class StubHttpResponse : HttpResponseData
    {
        public StubHttpResponse()
            : base(new StubFunctionContext())
        {
        }

        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();
        public override HttpCookies Cookies => new StubHttpCookies();
    }

    private sealed class StubHttpCookies : HttpCookies
    {
        public override void Append(string name, string value) { }
        public override void Append(IHttpCookie cookie) { }
        public override IHttpCookie CreateNew() => throw new NotImplementedException();
    }

    private sealed class StubFunctionContext : FunctionContext
    {
        public override string InvocationId => "test-invocation-id";
        public override string FunctionId => "test-function-id";
        public override TraceContext TraceContext => new StubTraceContext();
        public override BindingContext BindingContext => new StubBindingContext();
        public override RetryContext RetryContext => new StubRetryContext();
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition => new StubFunctionDefinition();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotImplementedException();
    }

    private sealed class StubTraceContext : TraceContext
    {
        public override string TraceParent => null!;
        public override string TraceState => null!;
    }

    private sealed class StubBindingContext : BindingContext
    {
        public override IReadOnlyDictionary<string, object?> BindingData => new Dictionary<string, object?>();
    }

    private sealed class StubRetryContext : RetryContext
    {
        public override int RetryCount => 0;
        public override int MaxRetryCount => 1;
    }

    private sealed class StubFunctionDefinition : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => ImmutableArray<FunctionParameter>.Empty;
        public override string PathToAssembly => "test";
        public override string EntryPoint => "test";
        public override string Id => "test";
        public override string Name => "test";
        public override IImmutableDictionary<string, BindingMetadata> InputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
        public override IImmutableDictionary<string, BindingMetadata> OutputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
    }
}
