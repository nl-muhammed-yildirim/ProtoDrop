using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using wa.infrastructure.Persistence;

namespace wa.infrastructure.Events;

/// <summary>
/// T-006b DI registration for the event backbone: the <c>WaDbContext</c>
/// (registration lives in <c>PersistenceDependency</c>), the
/// <c>ServiceBusClient</c> (null locally — AGENT.md §5.2 "in-memory fake"),
/// the <c>ServiceBusTopicOptions</c>, and <c>IEventPublisher</c> wired to
/// <see cref="SbEventPublisher"/>.
/// </summary>
public static class EventPublishingDependency
{
    private const string DefaultTopic = "core";

    /// <summary>
    /// Registers <c>WaDbContext</c>,
    /// <c>ServiceBusClient</c> (null when <c>Wa:ServiceBus:ConnectionString</c>
    /// is empty — local-dev fake, AGENT.md §5.2), <c>ServiceBusTopicOptions</c>
    /// (topic from <c>Wa:ServiceBus:Topic</c>, frozen default <c>core</c>, TA-5.1),
    /// and <c>IEventPublisher</c> → <see cref="SbEventPublisher"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Host configuration.</param>
    public static IServiceCollection AddEventPublishing(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWaDbContext(configuration);

        var connection = configuration.GetSection("Wa:ServiceBus:ConnectionString").Value;
        if (string.IsNullOrWhiteSpace(connection))
        {
            // Null singleton instance → ctor injection hands back null.
            // Local-dev fake per AGENT.md §5.2.
            services.AddSingleton((ServiceBusClient?)null);
        }
        else
        {
            services.AddSingleton(new ServiceBusClient(connection));
        }

        var topic = configuration.GetSection("Wa:ServiceBus:Topic").Value;
        services.AddSingleton(new ServiceBusTopicOptions(
            string.IsNullOrWhiteSpace(topic) ? DefaultTopic : topic));

        services.AddScoped<IEventPublisher, SbEventPublisher>();
        return services;
    }
}
