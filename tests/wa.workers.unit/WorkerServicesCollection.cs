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

/// <summary>Defines a non-parallel collection: the env-var fact mutates process-wide state.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerServicesCollection
{
    public const string Name = "WorkerServices (process env)";
}
