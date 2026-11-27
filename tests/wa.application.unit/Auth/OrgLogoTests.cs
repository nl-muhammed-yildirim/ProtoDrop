using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.unit.Auth;

// US-021-04: RemoveOrgLogoHandler (AC-021-5), UploadOrgLogoHandler (FR-021-3..5, AC-021-4, EC-021-3) and GetOrgLogoHandler (EC-021-5 owner half).
// Hand-stub ports record into one ordered call list.
public class OrgLogoTests
{
    private const long TestLimit = 64; // small test data; the production value comes from the registry
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly List<string> _calls = [];

    private (RemoveOrgLogoHandler Handler, Guid UserId) Build(string? path, bool userExists = true)
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        if (userExists)
            users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = path };
        return (new RemoveOrgLogoHandler(users, new StubLogos(_calls), new StubBlobs(_calls)), userId);
    }

    private (UploadOrgLogoHandler Handler, Guid UserId, StubLogos Logos) BuildUpload(
        string? previousPath = null, bool branding = true, bool userExists = true, bool setPathThrows = false)
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        if (userExists)
            users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = previousPath };
        var blobs = new StubBlobs(_calls);
        var logos = new StubLogos(_calls) { Throw = setPathThrows };
        var handler = new UploadOrgLogoHandler(
            users, new StubPlan(branding), blobs, blobs, logos, new StubMinter());
        return (handler, userId, logos);
    }

    private static byte[] Png(int total)
    {
        var bytes = new byte[total];
        PngHead.CopyTo(bytes, 0);
        return bytes;
    }

    [Fact]
    public async Task Remove_PathSet_DeletesBlobThenClearsColumn()
    {
        var (handler, userId) = Build("transfers/x/logo");

        await handler.Handle(new RemoveOrgLogoCommand(userId), CancellationToken.None);

        Assert.Equal(["Delete(transfers/x/logo)", "SetPath(null)"], _calls);
    }

    [Fact]
    public async Task Remove_BlobDeleteFails_LeavesPathSetAndRethrows()
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = "transfers/x/logo" };
        var blobs = new StubBlobs(_calls) { ThrowOnDelete = true };
        var handler = new RemoveOrgLogoHandler(users, new StubLogos(_calls), blobs);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RemoveOrgLogoCommand(userId), CancellationToken.None));

        Assert.DoesNotContain(_calls, c => c.StartsWith("SetPath("));
        Assert.Equal("transfers/x/logo", users.Users[userId].OrgLogoBlobPath);
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

    [Fact]
    public async Task Upload_ProPng_UploadsThenSetsPath()
    {
        var (handler, userId, _) = BuildUpload();
        var canonical = BlobPaths.OrgLogo(userId);

        var view = await handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None);

        Assert.Equal([$"Upload({canonical},image/png,20)", $"SetPath({canonical})"], _calls);
        Assert.NotNull(view.LogoUrl);
        Assert.Equal(TestLimit, view.MaxLogoSize);
        Assert.True(view.BrandingEnabled);
    }

    [Fact]
    public async Task Upload_ExactlyAtLimit_Accepted()
    {
        var (handler, userId, _) = BuildUpload();

        await handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png((int)TestLimit))), CancellationToken.None);

        Assert.Equal(2, _calls.Count);
    }

    [Fact]
    public async Task Upload_OverLimit_ThrowsLogoTooLarge_NoWrites() // AC-021-4
    {
        var (handler, userId, _) = BuildUpload();

        var ex = await Assert.ThrowsAsync<OrgLogoRejectedException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png((int)TestLimit + 1))), CancellationToken.None));

        Assert.Equal(ProblemCodes.LogoTooLarge, ex.Code);
        Assert.Contains(TestLimit.ToString(), ex.Message);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Upload_Gif_ThrowsLogoTypeInvalid_NoWrites() // FR-021-5
    {
        var (handler, userId, _) = BuildUpload();
        var gif = "GIF89a"u8.ToArray();

        var ex = await Assert.ThrowsAsync<OrgLogoRejectedException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(gif)), CancellationToken.None));

        Assert.Equal(ProblemCodes.LogoTypeInvalid, ex.Code);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Upload_Empty_ThrowsLogoTypeInvalid()
    {
        var (handler, userId, _) = BuildUpload();

        var ex = await Assert.ThrowsAsync<OrgLogoRejectedException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream()), CancellationToken.None));

        Assert.Equal(ProblemCodes.LogoTypeInvalid, ex.Code);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Upload_FreePlan_ThrowsForbidden_BeforeReadingBody() // FR-021-4
    {
        var (handler, userId, _) = BuildUpload(branding: false);
        var body = new ThrowingStream();

        var ex = await Assert.ThrowsAsync<OrgLogoRejectedException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, body), CancellationToken.None));

        Assert.Equal(ProblemCodes.Forbidden, ex.Code);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Upload_Svg_StoredAsImageSvgXml() // EC-021-3
    {
        var (handler, userId, _) = BuildUpload();
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

        await handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(svg)), CancellationToken.None);

        Assert.Equal($"Upload({BlobPaths.OrgLogo(userId)},image/svg+xml,{svg.Length})", _calls[0]);
    }

    [Fact]
    public async Task Upload_SetPathFails_NoPreviousPath_DeletesNewBlobAndRethrows() // no partial state
    {
        var (handler, userId, _) = BuildUpload(setPathThrows: true);
        var canonical = BlobPaths.OrgLogo(userId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None));

        Assert.Equal([$"Upload({canonical},image/png,20)", $"SetPath({canonical})", $"Delete({canonical})"], _calls);
    }

    [Fact]
    public async Task Upload_SetPathAffectsNoRow_DeletesNewBlobAndThrowsNotFound() // concurrent soft-delete
    {
        var (handler, userId, logos) = BuildUpload();
        logos.Matched = false;
        var canonical = BlobPaths.OrgLogo(userId);

        var ex = await Assert.ThrowsAsync<MeProfileException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None));

        Assert.Equal(ProblemCodes.TitleOf(ProblemCodes.NotFound), ex.Message);
        Assert.Equal([$"Upload({canonical},image/png,20)", $"SetPath({canonical})", $"Delete({canonical})"], _calls);
    }

    [Fact]
    public async Task Upload_PreviousIsCanonical_SetPathAffectsNoRow_DeletesBlobAndThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        var canonical = BlobPaths.OrgLogo(userId);
        var users = new StubUsers();
        users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = canonical };
        var blobs = new StubBlobs(_calls);
        var logos = new StubLogos(_calls) { Matched = false };
        var handler = new UploadOrgLogoHandler(users, new StubPlan(true), blobs, blobs, logos, new StubMinter());

        await Assert.ThrowsAsync<MeProfileException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None));

        Assert.Equal([$"Upload({canonical},image/png,20)", $"SetPath({canonical})", $"Delete({canonical})"], _calls);
    }

    [Fact]
    public async Task Upload_ReplacesLegacyPath_DeletesOldBlobAfterSetPath()
    {
        var (handler, userId, _) = BuildUpload(previousPath: "transfers/legacy/logo");
        var canonical = BlobPaths.OrgLogo(userId);

        await handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None);

        Assert.Equal([$"Upload({canonical},image/png,20)", $"SetPath({canonical})", "Delete(transfers/legacy/logo)"], _calls);
    }

    [Fact]
    public async Task Upload_SameCanonicalPath_NoDelete()
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = BlobPaths.OrgLogo(userId) };
        var blobs = new StubBlobs(_calls);
        var handler = new UploadOrgLogoHandler(users, new StubPlan(true), blobs, blobs, new StubLogos(_calls), new StubMinter());

        await handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None);

        Assert.DoesNotContain(_calls, c => c.StartsWith("Delete("));
    }

    [Fact]
    public async Task Upload_UnknownUser_ThrowsNotFound()
    {
        var (handler, userId, _) = BuildUpload(userExists: false);

        var ex = await Assert.ThrowsAsync<MeProfileException>(() =>
            handler.Handle(new UploadOrgLogoCommand(userId, new MemoryStream(Png(20))), CancellationToken.None));

        Assert.Equal(ProblemCodes.TitleOf(ProblemCodes.NotFound), ex.Message);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task GetLogo_FreeWithKeptPath_ReturnsUrl_BrandingDisabled() // EC-021-5 owner half
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A", OrgLogoBlobPath = BlobPaths.OrgLogo(userId) };
        var handler = new GetOrgLogoHandler(users, new StubPlan(false), new StubMinter());

        var view = await handler.Handle(new GetOrgLogoQuery(userId), CancellationToken.None);

        Assert.NotNull(view.LogoUrl);
        Assert.False(view.BrandingEnabled);
        Assert.Equal(TestLimit, view.MaxLogoSize);
    }

    [Fact]
    public async Task GetLogo_NoPath_UrlNull()
    {
        var userId = Guid.NewGuid();
        var users = new StubUsers();
        users.Users[userId] = new AppUser { Id = userId, Email = "a@example.com", DisplayName = "A" };
        var handler = new GetOrgLogoHandler(users, new StubPlan(true), new StubMinter());

        var view = await handler.Handle(new GetOrgLogoQuery(userId), CancellationToken.None);

        Assert.Null(view.LogoUrl);
        Assert.True(view.BrandingEnabled);
    }

    private sealed class StubUsers : IAppUserStore
    {
        public Dictionary<Guid, AppUser> Users { get; } = new();
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult<AppUser?>(null);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct) => Task.FromResult(true);
        public Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class StubLogos(List<string> calls) : IOrgLogoStore
    {
        public bool Throw { get; set; }
        public bool Matched { get; set; } = true;

        public Task<bool> SetPathAsync(Guid userId, string? blobPath, CancellationToken ct)
        {
            calls.Add($"SetPath({blobPath ?? "null"})");
            if (Throw)
                throw new InvalidOperationException("db down");
            return Task.FromResult(Matched);
        }
    }

    private sealed class StubBlobs(List<string> calls) : IBlobStore, IBlobContentWriter
    {
        public bool ThrowOnDelete { get; set; }

        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            calls.Add($"Upload({path},{contentType},{content.Length})");
            return Task.CompletedTask;
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDelete)
                throw new InvalidOperationException("storage down");
            calls.Add($"Delete({path})");
            return Task.CompletedTask;
        }
    }

    private sealed class StubPlan(bool branding) : IPlanContextAccessor
    {
        public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanContext(
                branding ? "pro" : "free",
                new LimitsRecord { MaxLogoSize = TestLimit },
                new PlanFeatures { Branding = branding }));
    }

    private sealed class StubMinter : IOrgLogoSasMinter
    {
        public Uri MintOrgLogo(Guid userId) => new($"https://acct.example/avatars/{userId:N}/logo?sig=x");
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("body must not be read");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("body must not be read");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
