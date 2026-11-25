using MediatR;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-2): GET /api/v1/auth/me/export — latest export state, null = none.</summary>
/// <param name="UserId">Identifier of the user of the get data export query.</param>
public sealed record GetDataExportQuery(Guid UserId) : IRequest<DataExportView?>;
