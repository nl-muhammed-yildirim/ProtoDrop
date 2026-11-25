using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Runtime.CompilerServices;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.application.UseCases.Features;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.application.UseCases.Transfers;
using wa.domain; // T-006-03: EmailSuppression entity type for admin routes
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.domain.Telemetry; // T-006-03: admin_action event name for suppression removal
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.api.Middleware;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace wa.api.Endpoints;

/// <summary>Formatting helpers shared by several endpoint groups.</summary>
internal static class EndpointFormatting
{
    // EF reads datetime2 back as Kind=Unspecified (no trailing Z → browsers parse as local time); US-047 wire timestamps are UTC by contract.
    /// <summary>Marks a database <see cref="DateTime"/> as UTC (SQL Server returns Kind=Unspecified) so it serialises with a trailing Z.</summary>
    /// <param name="value">The value read from the database.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    public static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
