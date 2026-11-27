using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using wa.api;
using wa.application.Ports;
using wa.infrastructure.Events;

namespace wa.api.integration.Events;

/// <summary>
/// CHORE-local-email (TA-0.2 rule 7): DI picks the <see cref="IEventPublisher"/> from configuration alone. Needs no containers:
/// the registrations are built but no database or blob call is made.
/// </summary>
public class ApiEventPublisherSelectionTests
{
    private const string SbConnection = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZQ==";
    private const string BaseUrl = "http://host.docker.internal:8081";
    private const string SignKey = "local-email-handoff-key-dev-only";

    private static Dictionary<string, string?> BaseSettings() => new()
    {
        ["ConnectionStrings:WaDb"] = "Server=localhost;Database=wa;TrustServerCertificate=True",
        ["Wa:Blob:AccountUrl"] = "http://127.0.0.1:10000/devstoreaccount1",
        ["Wa:Blob:AccountKey"] = Convert.ToBase64String(new byte[64]),
        ["Wa:Jwt:Secret"] = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddApiServices(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void ServiceBusKeySet_KeepsSbEventPublisher_EvenWithBaseUrl()
    {
        var settings = BaseSettings();
        settings["Wa:ServiceBus:ConnectionString"] = SbConnection;
        settings[EmailHandOff.BaseUrlKey] = BaseUrl;
        settings[EmailHandOff.SignKeyKey] = SignKey;
        settings["ASPNETCORE_ENVIRONMENT"] = "Development";
        using var sp = Build(settings);

        Assert.IsType<SbEventPublisher>(sp.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public void NothingSet_UsesInMemoryEventPublisher()
    {
        using var sp = Build(BaseSettings());

        Assert.IsType<InMemoryEventPublisher>(sp.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public void BaseUrlAndKey_InDevelopment_UsesHttpHandOffEventPublisher()
    {
        var settings = BaseSettings();
        settings[EmailHandOff.BaseUrlKey] = BaseUrl;
        settings[EmailHandOff.SignKeyKey] = SignKey;
        settings["ASPNETCORE_ENVIRONMENT"] = "Development";
        using var sp = Build(settings);

        Assert.IsType<HttpHandOffEventPublisher>(sp.GetRequiredService<IEventPublisher>());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData(null)]
    public void BaseUrl_OutsideLocalOrDevelopment_Throws(string? environment)
    {
        var settings = BaseSettings();
        settings[EmailHandOff.BaseUrlKey] = BaseUrl;
        settings[EmailHandOff.SignKeyKey] = SignKey;
        settings["ASPNETCORE_ENVIRONMENT"] = environment;

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Wa:EmailFunction:BaseUrl", ex.Message);
        Assert.DoesNotContain(SignKey, ex.Message);
    }

    [Fact]
    public void BaseUrl_WithoutSignKey_Throws()
    {
        var settings = BaseSettings();
        settings[EmailHandOff.BaseUrlKey] = BaseUrl;
        settings["ASPNETCORE_ENVIRONMENT"] = "Development";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Wa:Email:HandOffSignKey", ex.Message);
    }
}
