using MediatR;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-1): POST /api/v1/auth/me/export — request an export; 202 body is the processing view.</summary>
public sealed record ExportMyDataCommand(Guid UserId) : IRequest<DataExportView>;
