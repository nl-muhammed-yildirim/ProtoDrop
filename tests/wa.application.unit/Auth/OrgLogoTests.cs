using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.unit.Auth;

// US-021-04 (AC-021-5, unit half): RemoveOrgLogoHandler. Hand-stub ports record into one ordered call list.
public class OrgLogoTests
{
    private readonly List<string> _calls = [];

    private (RemoveOrgLogoHandler Handler, Guid UserId) Build(string? path, bool userExists = true)
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        if (userExists)
            users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = path };
        return (new RemoveOrgLogoHandler(users, new StubLogos(_calls), new StubBlobs(_calls)), userId);
    }

    [Fact]
    public async Task Remove_PathSet_ClearsColumnThenDeletesBlob()
    {
        var (handler, userId) = Build("transfers/x/logo");

        await handler.Handle(new RemoveOrgLogoCommand(userId), CancellationToken.None);

        Assert.Equal(["SetPath(null)", "Delete(transfers/x/logo)"], _calls);
    }

    [Fact]
    public async Task Remove_NoPath_NoOp()
    {
        var (handler, userId) = Build(null);

        await handler.Handle(new RemoveOrgLogoCommand(userId), CancellationToken.None);

        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Remove_UnknownUser_ThrowsNotFound()
    {
        var (handler, userId) = Build(null, userExists: false);

        var ex = await Assert.ThrowsAsync<MeProfileException>(() =>
            handler.Handle(new RemoveOrgLogoCommand(userId), CancellationToken.None));

        Assert.Equal(ProblemCodes.TitleOf(ProblemCodes.NotFound), ex.Message);
        Assert.Empty(_calls);
    }

    private sealed class StubUsers : IAppUserStore
    {
        public Dictionary<Guid, AppUser> Users { get; } = new();
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult<AppUser?>(null);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class StubLogos(List<string> calls) : IOrgLogoStore
    {
        public Task SetPathAsync(Guid userId, string? blobPath, CancellationToken ct)
        {
            calls.Add($"SetPath({blobPath ?? "null"})");
            return Task.CompletedTask;
        }
    }

    private sealed class StubBlobs(List<string> calls) : IBlobStore
    {
        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            calls.Add($"Delete({path})");
            return Task.CompletedTask;
        }
    }
}
