using MediatR;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01: creates a Processing row unless one is already in flight (EC-047-6); failed/ready rows never block (EC-047-2).</summary>
/// <param name="users">Store used to check that the account exists.</param>
/// <param name="store">Store that holds the export rows.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed class ExportMyDataHandler(
    IAppUserStore users,
    IDataExportStore store,
    TimeProvider timeProvider) : IRequestHandler<ExportMyDataCommand, DataExportView>
{
    private readonly IAppUserStore _users = users;
    private readonly IDataExportStore _store = store;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<DataExportView> Handle(ExportMyDataCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        var latest = await _store.GetLatestAsync(request.UserId, cancellationToken);
        if (latest is { State: DataExportState.Processing })
            throw new DataExportInFlightException();

        var row = new wa.domain.DataExport
        {
            Id = Guid.NewGuid(),
            AppUserId = request.UserId,
            State = DataExportState.Processing,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };
        // The filtered unique index makes the race loser fail the insert (EC-047-6).
        if (!await _store.TryCreateAsync(row, cancellationToken))
            throw new DataExportInFlightException();

        return new DataExportView(DataExportState.WireName(row.State), row.CreatedAtUtc, null, null);
    }
}
