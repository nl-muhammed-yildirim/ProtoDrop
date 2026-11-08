using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Correlation;
using wa.workers;
using Xunit;

namespace wa.workers.unit;

/// <summary>
/// T-012-01 (AC-012-1): ZipFunction correlation tests — verifies the MEL BeginScope pattern
/// captures the W3C traceparent from inbound headers or generates a fresh one when absent.
/// Only the scope-under-test is exercised; FunctionContext and the auth/processing path are
/// not touched, so minimal HttpRequestData stubs suffice (context is passed as null).
/// </summary>
public class ZipFunctionCorrelationTests
{
    private const string ValidTraceParent = "00-a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6-0af1bc2d3e4f5a6b-07";

    // ---- Test ILogger that captures the last BeginScope dictionary ----

    private class ScopeCapturingLogger : ILogger<ZipFunction>
    {
        public Dictionary<string, object?>? LastScope { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IDictionary<string, object?> dict)
                LastScope = new Dictionary<string, object?>(dict);
            return NullDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    // ---- Hand stubs for the constructor dependencies (mirror wa.workers.unit conventions; no Moq) ----

    private class StubBlobStore : IBlobStore
    {
        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream();
            return Task.FromResult<Stream>(stream);
        }
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class StubBlobSasMinter : IBlobSasMinter
    {
        public Uri MintDraftUpload(string draftId, string fileId) => new Uri("https://example.com/draft");
        public Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition) => new Uri("https://example.com/file");
        public Uri MintRecipientZip(string transferId) => new Uri("https://example.com/zip");
    }

    private class StubZipFileSource : IZipFileSource
    {
        public Task<(bool Found, IReadOnlyList<PublicFileItem> Files)> GetFilesByTransferIdAsync(Guid transferId, CancellationToken ct)
        {
            (bool Found, IReadOnlyList<PublicFileItem> Files) result = (false, Array.Empty<PublicFileItem>());
            return Task.FromResult(result);
        }
    }

    private class StubLimitsProvider : ILimitsProvider
    {
        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LimitsRecord());
    }

    private class StubConfig : IConfiguration
    {
        private readonly Dictionary<string, string?> _values = new();

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var value) ? value : null;
            set => _values[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
        public IConfigurationSection GetSection(string key) => throw new NotImplementedException();
        public void Reload() { }
        public IChangeToken GetReloadToken() => new NoopChangeToken();
    }

    private sealed class NoopChangeToken : Microsoft.Extensions.Primitives.IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    // ---- Minimal Azure Functions SDK stubs: only Headers + CreateResponse are used in the scope-under-test path ----

    private class StubHttpRequest : HttpRequestData
    {
        private readonly HttpHeadersCollection _headers = new();

        public StubHttpRequest(string? traceparent)
            : base(StubFunctionContext.Instance)
        {
            if (traceparent is not null)
                _headers.Add("traceparent", traceparent);
        }

        public override Stream Body => new MemoryStream();
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override HttpHeadersCollection Headers => _headers;
        public override IEnumerable<ClaimsIdentity> Identities => Enumerable.Empty<ClaimsIdentity>();
        public override string Method => "POST";
        public override Uri Url => new("https://example.com/zip");

        // HttpRequestData.CreateResponse() is parameterless — the framework allocates the response.
        public override HttpResponseData CreateResponse() => new StubHttpResponse();
    }

    private class StubHttpResponse : HttpResponseData
    {
        public StubHttpResponse()
            : base(StubFunctionContext.Instance)
        {
        }

        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();
        // HttpResponseData.Cookies has no setter — must be an expression-bodied override.
        public override HttpCookies Cookies => new StubHttpCookies();
    }

    private class StubHttpCookies : HttpCookies
    {
        public override void Append(string name, string value) { }
        public override void Append(IHttpCookie cookie) { }
        public override IHttpCookie CreateNew() => throw new NotImplementedException();
    }

    // FunctionContext is abstract (no ctor usable in tests) — minimal stub; only required by the base HttpRequestData ctor.
    private sealed class StubFunctionContext : FunctionContext
    {
        public static readonly StubFunctionContext Instance = new();

        public override string InvocationId => "test-invocation-id";
        public override string FunctionId => "test-function-id";
        public override TraceContext TraceContext => new StubTraceContext();
        public override BindingContext BindingContext => new StubBindingContext();
        public override RetryContext RetryContext => new StubRetryContext();
        public override IServiceProvider? InstanceServices { get; set; }
        public override FunctionDefinition FunctionDefinition => new StubFunctionDefinition();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotImplementedException();
    }

    private sealed class StubTraceContext : TraceContext
    {
        public override string? TraceParent => null;
        public override string? TraceState => null;
    }

    private sealed class StubBindingContext : BindingContext
    {
        public override IReadOnlyDictionary<string, object> BindingData => new Dictionary<string, object>();
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

    // ---- Fixtures ----

    private static (ZipFunction Function, ScopeCapturingLogger Logger) Build(string? traceparent)
    {
        var logger = new ScopeCapturingLogger();
        var config = new StubConfig();
        config["Wa:Zip:SignKey"] = "test-sign-key";

        // WaTelemetryReporter is sealed (not an interface) — construct a real instance; it is not invoked in this path.
        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[] { 0 });

        var function = new ZipFunction(
            new StubBlobStore(),
            new StubBlobSasMinter(),
            new StubZipFileSource(),
            new StubLimitsProvider(),
            telemetry,
            config,
            logger);

        return (function, logger);
    }

    // ---- Tests ----

    [Fact]
    public async Task Run_InboundValidTraceparent_ScopesLoggerWithInboundValues()
    {
        var (function, logger) = Build(ValidTraceParent);
        var request = new StubHttpRequest(ValidTraceParent);

        await function.Run(null!, request, "test-transfer-id");

        Assert.NotNull(logger.LastScope);
        Assert.Equal("a1b2c3d4", logger.LastScope!["CorrelationId"]);
        Assert.Equal(ValidTraceParent, logger.LastScope["TraceParent"]);
    }

    [Fact]
    public async Task Run_NoInboundTraceparent_GeneratesFreshValidTraceParent()
    {
        var (function, logger) = Build(null);
        var request = new StubHttpRequest(null);

        await function.Run(null!, request, "test-transfer-id");

        Assert.NotNull(logger.LastScope);
        var correlationId = logger.LastScope!["CorrelationId"] as string;
        var traceParent = logger.LastScope["TraceParent"] as string;

        Assert.NotNull(correlationId);
        Assert.Equal(8, correlationId.Length);
        Assert.All(correlationId, c => Assert.True(char.IsDigit(c) || (c is >= 'a' and <= 'f')));

        Assert.NotNull(traceParent);
        var parsed = W3CTraceParser.Parse(traceParent!);
        Assert.NotNull(parsed);
        Assert.Equal(correlationId, parsed!.CorrelationId);
    }

    [Fact]
    public async Task Run_MalformedTraceparent_GeneratesFreshValidTraceParent()
    {
        var (function, logger) = Build("garbage-value");
        var request = new StubHttpRequest("garbage-value");

        await function.Run(null!, request, "test-transfer-id");

        Assert.NotNull(logger.LastScope);
        var correlationId = logger.LastScope!["CorrelationId"] as string;
        var traceParent = logger.LastScope["TraceParent"] as string;

        Assert.NotNull(correlationId);
        Assert.Equal(8, correlationId.Length);

        Assert.NotNull(traceParent);
        var parsed = W3CTraceParser.Parse(traceParent!);
        Assert.NotNull(parsed);
        // A fresh (generated) value must NOT equal the inbound garbage.
        Assert.NotEqual("garbage-value", traceParent);
        Assert.Equal(correlationId, parsed!.CorrelationId);
    }
}
