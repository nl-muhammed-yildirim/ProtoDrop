using MediatR;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-1): POST /api/v1/auth/me/export — request an export; 202 body is the processing view.</summary>
/// <param name="UserId">Id of the signed-in user who requests the export.</param>
public sealed record ExportMyDataCommand(Guid UserId) : IRequest<DataExportView>;
