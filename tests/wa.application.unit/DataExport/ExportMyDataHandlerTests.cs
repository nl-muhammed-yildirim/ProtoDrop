using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.application.UseCases.DataExport;
using wa.domain;
using Export = wa.domain.DataExport;

namespace wa.application.unit.DataExport;

/// <summary>US-047-01 (EC-047-2, EC-047-6): request + read handlers over in-memory stubs.</summary>
public class ExportMyDataHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class StubUsers(AppUser? user) : IAppUserStore
    {
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(user);
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateAsync(AppUser u, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct) => Task.FromResult(true);
        public Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubStore : IDataExportStore
    {
        public Export? Latest { get; set; }
        public bool CreateResult { get; set; } = true;
        public List<Export> Created { get; } = [];
        public Task<Export?> GetLatestAsync(Guid userId, CancellationToken ct) => Task.FromResult(Latest);
        public Task<bool> TryCreateAsync(Export row, CancellationToken ct)
        {
            if (!CreateResult) return Task.FromResult(false);
            Created.Add(row);
            return Task.FromResult(true);
        }
    }

    private sealed class StubMinter : IExportSasMinter
    {
        public Uri MintExportDownload(Guid userId, Guid exportId) => new($"https://blob.test/exports/{userId:N}/{exportId:N}.zip?sig=x");
    }

    private static ExportMyDataHandler Requester(StubStore store, bool userExists = true)
        => new(new StubUsers(userExists ? new AppUser { Id = UserId } : null), store, TimeProvider.System);

    private static GetDataExportHandler Reader(StubStore store) => new(store, new StubMinter(), TimeProvider.System);

    [Fact]
    public async Task Request_NoRow_CreatesProcessingRow()
    {
        var store = new StubStore();
        var view = await Requester(store).Handle(new ExportMyDataCommand(UserId), default);
        Assert.Equal("processing", view.State);
        var row = Assert.Single(store.Created);
        Assert.Equal(UserId, row.AppUserId);
        Assert.Equal(DataExportState.Processing, row.State);
    }

    [Fact]
    public async Task Request_LatestProcessing_ThrowsInFlight_NoCreate() // EC-047-6
    {
        var store = new StubStore { Latest = new Export { Id = Guid.NewGuid(), AppUserId = UserId, State = DataExportState.Processing } };
        await Assert.ThrowsAsync<DataExportInFlightException>(() => Requester(store).Handle(new ExportMyDataCommand(UserId), default));
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task Request_TryCreateFalse_ThrowsInFlight() // EC-047-6 race
    {
        var store = new StubStore { CreateResult = false };
        await Assert.ThrowsAsync<DataExportInFlightException>(() => Requester(store).Handle(new ExportMyDataCommand(UserId), default));
    }

    [Fact]
    public async Task Request_LatestFailed_CreatesNewRow() // EC-047-2
    {
        var store = new StubStore { Latest = new Export { Id = Guid.NewGuid(), AppUserId = UserId, State = DataExportState.Failed } };
        await Requester(store).Handle(new ExportMyDataCommand(UserId), default);
        Assert.Single(store.Created);
    }

    [Fact]
    public async Task Request_UnknownUser_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<MeProfileException>(() =>
            Requester(new StubStore(), userExists: false).Handle(new ExportMyDataCommand(UserId), default));
    }

    [Fact]
    public async Task Get_None_ReturnsNull()
        => Assert.Null(await Reader(new StubStore()).Handle(new GetDataExportQuery(UserId), default));

    [Fact]
    public async Task Get_Processing_HasNoUrl()
    {
        var store = new StubStore { Latest = new Export { Id = Guid.NewGuid(), AppUserId = UserId, State = DataExportState.Processing, CreatedAtUtc = DateTime.UtcNow } };
        var view = await Reader(store).Handle(new GetDataExportQuery(UserId), default);
        Assert.Equal("processing", view!.State);
        Assert.Null(view.DownloadUrl);
    }

    [Fact]
    public async Task Get_Ready_ReturnsMintedUrl()
    {
        var id = Guid.NewGuid();
        var store = new StubStore { Latest = new Export { Id = id, AppUserId = UserId, State = DataExportState.Ready, CreatedAtUtc = DateTime.UtcNow, ReadyAtUtc = DateTime.UtcNow } };
        var view = await Reader(store).Handle(new GetDataExportQuery(UserId), default);
        Assert.Equal("ready", view!.State);
        Assert.Contains($"/exports/{UserId:N}/{id:N}.zip", view.DownloadUrl!.ToString());
    }

    [Fact]
    public async Task Get_ReadyOlderThanSevenDays_ReturnsNull()
    {
        var store = new StubStore { Latest = new Export { Id = Guid.NewGuid(), AppUserId = UserId, State = DataExportState.Ready, CreatedAtUtc = DateTime.UtcNow.AddDays(-9), ReadyAtUtc = DateTime.UtcNow.AddDays(-8) } };
        Assert.Null(await Reader(store).Handle(new GetDataExportQuery(UserId), default));
    }

    [Fact]
    public async Task Get_Failed_ReturnsFailedState()
    {
        var store = new StubStore { Latest = new Export { Id = Guid.NewGuid(), AppUserId = UserId, State = DataExportState.Failed, CreatedAtUtc = DateTime.UtcNow } };
        var view = await Reader(store).Handle(new GetDataExportQuery(UserId), default);
        Assert.Equal("failed", view!.State);
        Assert.Null(view.DownloadUrl);
    }
}
