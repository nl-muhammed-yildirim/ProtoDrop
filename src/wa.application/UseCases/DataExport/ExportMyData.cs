using MediatR;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-1): POST /api/v1/auth/me/export — request an export; 202 body is the processing view.</summary>
public sealed record ExportMyDataCommand(Guid UserId) : IRequest<DataExportView>;

/// <summary>US-047-01 (FR-047-2): GET /api/v1/auth/me/export — latest export state, null = none.</summary>
public sealed record GetDataExportQuery(Guid UserId) : IRequest<DataExportView?>;

/// <summary>Wire view: DownloadUrl is set only when State is "ready".</summary>
public sealed record DataExportView(string State, DateTime CreatedAtUtc, DateTime? ReadyAtUtc, Uri? DownloadUrl);

/// <summary>US-047-01: architecture constants for the export feature (TA-6.3 style, not plan limits). D-22 retention default.</summary>
public static class DataExportPolicy
{
    /// <summary>How long a ready zip is kept before the f-data-export purge pass deletes it (D-22).</summary>
    public static readonly TimeSpan StoredRetention = TimeSpan.FromDays(7);

    /// <summary>Rows processed / purged per job pass.</summary>
    public const int BatchSize = 50;
}

/// <summary>EC-047-6: an export is already processing for this user — endpoint maps to 409 EXPORT_IN_FLIGHT.</summary>
public sealed class DataExportInFlightException : Exception
{
    public DataExportInFlightException() : base("An export is already in progress.") { }
}

/// <summary>US-047-01: creates a Processing row unless one is already in flight (EC-047-6); failed/ready rows never block (EC-047-2).</summary>
public sealed class ExportMyDataHandler : IRequestHandler<ExportMyDataCommand, DataExportView>
{
    private readonly IAppUserStore _users;
    private readonly IDataExportStore _store;

    public ExportMyDataHandler(IAppUserStore users, IDataExportStore store)
    {
        _users = users;
        _store = store;
    }

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
            CreatedAtUtc = DateTime.UtcNow,
        };
        // The filtered unique index makes the race loser fail the insert (EC-047-6).
        if (!await _store.TryCreateAsync(row, cancellationToken))
            throw new DataExportInFlightException();

        return new DataExportView(DataExportState.WireName(row.State), row.CreatedAtUtc, null, null);
    }
}

/// <summary>US-047-01: reads the latest export; ready rows get a fresh 24 h SAS per call; ready rows past retention read as none.</summary>
public sealed class GetDataExportHandler : IRequestHandler<GetDataExportQuery, DataExportView?>
{
    private readonly IDataExportStore _store;
    private readonly IExportSasMinter _minter;

    public GetDataExportHandler(IDataExportStore store, IExportSasMinter minter)
    {
        _store = store;
        _minter = minter;
    }

    public async Task<DataExportView?> Handle(GetDataExportQuery request, CancellationToken cancellationToken)
    {
        var row = await _store.GetLatestAsync(request.UserId, cancellationToken);
        if (row is null)
            return null;

        Uri? url = null;
        if (row.State == DataExportState.Ready)
        {
            // Purge has not run yet but the zip is past its 7-day life: treat as gone.
            if (row.ReadyAtUtc is { } readyAt && DateTime.UtcNow - readyAt > DataExportPolicy.StoredRetention)
                return null;
            url = _minter.MintExportDownload(row.AppUserId, row.Id);
        }

        return new DataExportView(DataExportState.WireName(row.State), row.CreatedAtUtc, row.ReadyAtUtc, url);
    }
}
