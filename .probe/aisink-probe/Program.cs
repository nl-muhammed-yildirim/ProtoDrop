using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Microsoft.ApplicationInsights.Channel;
using EventT = Microsoft.ApplicationInsights.DataContracts.EventTelemetry;
using MetricT = Microsoft.ApplicationInsights.DataContracts.MetricTelemetry;
using TelemetryConf = Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration;
using TClient = Microsoft.ApplicationInsights.TelemetryClient;

var captured = new List<ITelemetry>();
var configuration = new TelemetryConf();
configuration.TelemetryChannel = new FakeChannel(captured);
var client = new TClient(configuration);

ILogger serilog = new LoggerConfiguration()
    .WriteTo.ApplicationInsights(client, new EventTelemetryConverter(), LogEventLevel.Information)
    .CreateLogger();

var uploadProps = new List<LogEventProperty>
{
    new("bytes", new ScalarValue("1234")),
    new("durationMs", new ScalarValue("5")),
    new("retries", new ScalarValue("0")),
};

// ---- empirical TA-10.2 mapping checks ------------------------------------------

// Event: template text = closed-set event name; structured properties = TA-10.2 payload.
// TextToken makes the local rendered message show the event name as literal text.
serilog.Write(new LogEvent(
    DateTimeOffset.Now, LogEventLevel.Information, null,
    new MessageTemplate("upload_completed", new[] { new TextToken("upload_completed") }),
    uploadProps));

// Control: a plain free-form call.
serilog.Information("upload_started");

// ---- ground truth: which Write/Information overloads actually exist ----
foreach (System.Reflection.MethodInfo m in typeof(Serilog.ILogger).GetMethods())
{
    if (m.Name is not ("Information" or "Write" or "Fatal" or "Error" or "Warning" or "Debug" or "Verbose"))
        continue;
    System.Console.WriteLine($"{m.Name}([{string.Join(", ", m.GetParameters().Select(p => p.ParameterType.ToString()))}]");
}

await (serilog as IAsyncDisposable)!.DisposeAsync();
System.Console.WriteLine("total telemetry: " + captured.Count);
foreach (var e in captured.OfType<EventT>())
{
    System.Console.WriteLine($"EVENT Name='{e.Name}'");
    if (e.Properties != null)
        foreach (var kv in e.Properties.OrderBy(k => k.Key)) System.Console.WriteLine($"  PROP {kv.Key}={kv.Value}");
}

class FakeChannel : ITelemetryChannel
{
    private readonly List<ITelemetry> _items;
    public FakeChannel(List<ITelemetry> items) => _items = items;
    public string EndpointAddress { get; set; } = "fake";
    public bool? DeveloperMode { get; set; }
    public bool StayPut => true;
    public string EndpointName => "fake";
    public void Send(ITelemetry item) => _items.Add(item);
    public void SendTelemetry(ITelemetry telemetry) { }
    public void ProcessNextBatch() { }
    public void Flush() { }
    public void Shutdown() { }
    public void MarkForReprocess() { }
    public void OnOpen(Exception exception) { }
    public void OnClose(Exception exception) { }
    public void OnProcessingItem(ITelemetry item) { }
    public void Dispose() { }
}


