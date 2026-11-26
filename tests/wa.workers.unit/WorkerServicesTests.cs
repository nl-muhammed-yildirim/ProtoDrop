using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain.Telemetry;
using wa.infrastructure.Events;

namespace wa.workers.unit;

/// <summary>T-006-07: workers composition reads every setting through IConfiguration (Wa__X works on Linux).</summary>
[Collection(WorkerServicesCollection.Name)]
public class WorkerServicesTests
{
    private const string BlobUrl = "http://127.0.0.1:10000/devstoreaccount1";
    private const string JwtSecret = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string SbConnection = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZQ==";

    private static string BlobKey => Convert.ToBase64String(new byte[64]);

    private static Dictionary<string, string?> BaseSettings() => new()
    {
        ["WaDb"] = "Server=localhost;Database=wa;TrustServerCertificate=True",
        ["Wa:Blob:AccountUrl"] = BlobUrl,
        ["Wa:Blob:AccountKey"] = BlobKey,
        ["Wa:Jwt:Secret"] = JwtSecret,
    };

    private static ServiceProvider Build(IConfiguration c) =>
        new ServiceCollection().AddLogging().AddSingleton(c).AddWorkerServices(c).BuildServiceProvider();

    private static IConfiguration InMemory(Dictionary<string, string?> s) =>
        new ConfigurationBuilder().AddInMemoryCollection(s).Build();

    [Fact]
    public void ServiceBusKeyPresent_RegistersSbEventPublisher()
    {
        var s = BaseSettings();
        s["Wa:ServiceBus:ConnectionString"] = SbConnection;
        using var sp = Build(InMemory(s));

        Assert.IsType<SbEventPublisher>(sp.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public void ServiceBusKeyAbsent_RegistersInMemoryEventPublisher()
    {
        using var sp = Build(InMemory(BaseSettings()));

        Assert.IsType<InMemoryEventPublisher>(sp.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public void DataExportHandler_Resolves() // US-047-01: f-data-export builder is composed
    {
        using var sp = Build(InMemory(BaseSettings()));

        Assert.NotNull(sp.GetRequiredService<wa.application.UseCases.DataExport.BuildDataExportsHandler>());
    }

    [Fact]
    public void ActivateScheduledTransfersHandler_Resolves() // US-022-04: f-expire schedule sub-step is composed
    {
        using var sp = Build(InMemory(BaseSettings()));

        Assert.NotNull(sp.GetRequiredService<wa.application.UseCases.Transfers.ActivateScheduledTransfersHandler>());
    }

    [Fact]
    public void SendNotificationEmailsHandler_Resolves() // US-021-02: f-email branding deps are composed
    {
        var settings = BaseSettings();
        settings["Wa:Email:Sender"] = "log";
        settings["DOTNET_ENVIRONMENT"] = "Development"; // CHORE-deploy-hardening: log sender is allowed in dev only
        using var sp = Build(InMemory(settings));

        Assert.NotNull(sp.GetRequiredService<wa.application.UseCases.Transfers.SendNotificationEmailsHandler>());
    }

    [Fact]
    public void LogSender_InProduction_FailsAtComposition() // CHORE-deploy-hardening: fail fast, not on first send
    {
        var settings = BaseSettings();
        settings["Wa:Email:Sender"] = "log";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(InMemory(settings)));

        Assert.Contains("Wa:Email:Sender", ex.Message);
    }

    [Fact]
    public void GdprSweepHandler_Resolves() // US-047-02: f-gdpr-sweep is composed
    {
        using var sp = Build(InMemory(BaseSettings()));

        Assert.NotNull(sp.GetRequiredService<wa.application.UseCases.Gdpr.SweepErasedTelemetryHandler>());
    }

    [Fact]
    public void OwnerNotificationGate_Resolves() // F-XCT-003 (FR-046-3): the f-email send-time gate is composed with its store + flag resolver
    {
        using var sp = Build(InMemory(BaseSettings()));

        Assert.NotNull(sp.GetRequiredService<wa.application.UseCases.EmailSettings.OwnerNotificationGate>());
        Assert.IsType<wa.infrastructure.Persistence.EmailNotificationsStore>(sp.GetRequiredService<IEmailNotificationsStore>());
        Assert.NotNull(sp.GetRequiredService<IFlagResolver>()); // LimitsProvider implements both — the gate's flag source
    }

    [Fact]
    public void LinuxDoubleUnderscoreEnvVars_AreRead()
    {
        var vars = new Dictionary<string, string>
        {
            ["WaDb"] = "Server=localhost;Database=wa;TrustServerCertificate=True",
            ["Wa__Blob__AccountUrl"] = BlobUrl,
            ["Wa__Blob__AccountKey"] = BlobKey,
            ["Wa__Jwt__Secret"] = JwtSecret,
            ["Wa__ServiceBus__ConnectionString"] = SbConnection,
        };
        var previous = vars.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (k, v) in vars) Environment.SetEnvironmentVariable(k, v);
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            using var sp = Build(config);

            Assert.IsType<SbEventPublisher>(sp.GetRequiredService<IEventPublisher>());
            var uri = sp.GetRequiredService<IBlobSasMinter>().MintRecipientZip(Guid.NewGuid().ToString());
            Assert.Equal(new Uri(BlobUrl).Authority, uri.Authority);
            Assert.Equal(
                PiiTransforms.IpAddress("203.0.113.7", Encoding.UTF8.GetBytes(JwtSecret)),
                sp.GetRequiredService<WaTelemetryReporter>().IpSlot("203.0.113.7"));
        }
        finally
        {
            foreach (var (k, v) in previous) Environment.SetEnvironmentVariable(k, v);
        }
    }

    [Fact]
    public void MissingBlobAccountUrl_ThrowsNamingKey()
    {
        var s = BaseSettings();
        s.Remove("Wa:Blob:AccountUrl");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddWorkerServices(InMemory(s)));

        Assert.Contains("Wa:Blob:AccountUrl", ex.Message);
        Assert.DoesNotContain(BlobKey, ex.Message);
        Assert.DoesNotContain(JwtSecret, ex.Message);
    }

    [Fact]
    public void KeyNames_ArePinned()
    {
        Assert.Equal("WaDb", WorkerServices.DbKey);
        Assert.Equal("Wa:Blob:AccountUrl", WorkerServices.BlobAccountUrlKey);
        Assert.Equal("Wa:Blob:AccountKey", WorkerServices.BlobAccountKeyKey);
        Assert.Equal("Wa:Jwt:Secret", WorkerServices.JwtSecretKey);
        Assert.Equal("Wa:ServiceBus:ConnectionString", WorkerServices.ServiceBusKey);
    }
}
