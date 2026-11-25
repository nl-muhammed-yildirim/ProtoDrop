using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

// Shared across the factory host + tests (static — WebApplicationFactory caches one host per process).
public static class UnsubscribeTelemetryCapture
{
    public static readonly List<string> Lines = new();
}
