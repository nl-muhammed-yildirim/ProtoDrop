using MediatR;
using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01: reads the latest export; ready rows get a fresh 24 h SAS per call; ready rows past retention read as none.</summary>
public sealed class GetDataExportHandler : IRequestHandler<GetDataExportQuery, DataExportView?>
{
    private readonly IDataExportStore _store;
    private readonly IExportSasMinter _minter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="GetDataExportHandler"/> class.</summary>
    /// <param name="store">Store that holds the export rows.</param>
    /// <param name="minter">Mints the 24-hour download URL of a ready export.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public GetDataExportHandler(IDataExportStore store, IExportSasMinter minter, TimeProvider timeProvider)
    {
        _store = store;
        _minter = minter;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<DataExportView?> Handle(GetDataExportQuery request, CancellationToken cancellationToken)
    {
        var row = await _store.GetLatestAsync(request.UserId, cancellationToken);
        if (row is null)
            return null;

        Uri? url = null;
        if (row.State == DataExportState.Ready)
        {
            // Purge has not run yet but the zip is past its 7-day life: treat as gone.
            if (row.ReadyAtUtc is { } readyAt && _timeProvider.GetUtcNow().UtcDateTime - readyAt > DataExportPolicy.StoredRetention)
                return null;
            url = _minter.MintExportDownload(row.AppUserId, row.Id);
        }

        return new DataExportView(DataExportState.WireName(row.State), row.CreatedAtUtc, row.ReadyAtUtc, url);
    }
}
