using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>T-010-01: file item row projection (plain POCO — no EF entity in use case layer).</summary>
public sealed record FileItemRow(Guid Id, Guid BlobRefId, string OriginalName, long SizeBytes, string? ContentType, int SortOrder);
