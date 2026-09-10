using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using wa.infrastructure.Persistence;

namespace wa.infrastructure.Events;

/// <summary>
/// T-006b/c DI registration for the event backbone: the <c>WaDbContext</c>
/// (registration lives in <c>PersistenceDependency</c>), the
/// <c>ServiceBusClient</c> (only when <c>Wa:ServiceBus:ConnectionString</c>
/// is set; when unset the publisher is <see cref="InMemoryEventPublisher"/>,
/// AGENT.md §5.2), the <c>ServiceBusTopicOptions</c>, and
/// <c>IEventPublisher</c>.
/// </summary>
public static class EventPublishingDependency
{
    private const string DefaultTopic = "core";

    /// <summary>
    /// Registers <c>WaDbContext</c>, <c>ServiceBusClient</c> (only when
    /// <c>Wa:ServiceBus:ConnectionString</c> is set), <c>ServiceBusTopicOptions</c>
    /// (topic from <c>Wa:ServiceBus:Topic</c>, frozen default <c>core</c>, TA-5.1),
    /// and <c>IEventPublisher</c> → <see cref="InMemoryEventPublisher"/> (the
    /// fake, when the connection string is unset — AGENT.md §5.2) or →
    /// <see cref="SbEventPublisher"/> (real adapter, topic <c>core</c>, when set).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Host configuration.</param>
    public static IServiceCollection AddEventPublishing(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWaDbContext(configuration);

        var connection = configuration.GetSection("Wa:ServiceBus:ConnectionString").Value;
        var topic = configuration.GetSection("Wa:ServiceBus:Topic").Value;
        services.AddSingleton(new ServiceBusTopicOptions(
            string.IsNullOrWhiteSpace(topic) ? DefaultTopic : topic));

        if (string.IsNullOrWhiteSpace(connection))
        {
            // Local-dev: the fake IS the publisher (AGENT.md §5.2). Registered
            // as a singleton so tests can hold a reference and assert on
            // <see cref="InMemoryEventPublisher.Published"/>.
            // (No <c>ServiceBusClient</c> registration locally — only
            // <see cref="SbEventPublisher"/> injects it, and that type is
            // only registered in the real branch.)
            services.AddSingleton<InMemoryEventPublisher>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventPublisher>());
        }
        else
        {
            // SB set: the real adapter (TA-5.2 / TA-4.1.7).
            services.AddSingleton(new ServiceBusClient(connection));
            services.AddScoped<IEventPublisher, SbEventPublisher>();
        }
        return services;
    }
}