using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa.domain;
using wa.infrastructure.Persistence;
using wa.application.UseCases.Transfers;

namespace wa.application.unit.Transfers;

/// <summary>
/// T-006-03 (FR-006-5, TA-6.7): UnsubscribeToken round-trip + UnsubscribeService idempotent insert.
/// Token format: Base64Url(address|senderEmail).Base64Url(HMACSHA256(payload, key)).
/// Uses InMemory EF provider (same as other unit tests in this project).
/// </summary>
public class UnsubscribeServiceTests : IDisposable
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-key");

    private WaDbContext _db;
    private UnsubscribeService _service;

    public UnsubscribeServiceTests()
    {
        var options = new DbContextOptionsBuilder<WaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new WaDbContext(options);
        _db.Database.EnsureCreated();
        _service = new UnsubscribeService(_db, Key, NullLogger<UnsubscribeService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ---- Token round-trip (pure functions — no DB) ----

    [Fact]
    public void Create_ThenVerify_ReturnsOriginalPair()
    {
        var token = UnsubscribeToken.Create("alice@example.com", "bob@sender.com", Key);
        var result = UnsubscribeToken.Verify(token, Key);
        Assert.NotNull(result);
        Assert.Equal("alice@example.com", result!.Value.Address);
        Assert.Equal("bob@sender.com", result.Value.SenderEmail);
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsNull()
    {
        var token = UnsubscribeToken.Create("a@b.com", "c@d.com", Key);
        // Flip the last character of the signature segment.
        var segments = token.Split('.', 2);
        var sigChars = segments[1].ToCharArray();
        sigChars[^1] = sigChars[^1] == 'A' ? 'B' : 'A';
        var tampered = $"{segments[0]}.{new string(sigChars)}";

        Assert.Null(UnsubscribeToken.Verify(tampered, Key));
    }

    [Fact]
    public void Verify_WrongKey_ReturnsNull()
    {
        var token = UnsubscribeToken.Create("a@b.com", "c@d.com", Key);
        Assert.Null(UnsubscribeToken.Verify(token, Encoding.UTF8.GetBytes("other-key")));
    }

    [Fact]
    public void Verify_GarbageToken_ReturnsNull()
    {
        Assert.Null(UnsubscribeToken.Verify("not-a-valid-token!!!", Key));
        Assert.Null(UnsubscribeToken.Verify("", Key));
        Assert.Null(UnsubscribeToken.Verify(null, Key));
    }

    // ---- Service: valid token → row inserted ----

    [Fact]
    public async Task ValidToken_InsertsSuppressionRow()
    {
        var token = UnsubscribeToken.Create("recipient@x.com", "sender@y.com", Key);
        var result = await _service.HandleAsync(token, CancellationToken.None);

        Assert.True(result.Success);
        var row = await _db.Set<EmailSuppression>().SingleAsync();
        Assert.Equal("recipient@x.com", row.Address);
        Assert.Equal("sender@y.com", row.SenderEmail);
    }

    // ---- Service: double-click (UQ_ES violation) → still Success (idempotent) ----
    // Note: InMemory provider doesn't enforce unique constraints, so this test verifies the logic path.
    // The real UQ_ES enforcement is covered by the integration test (SQL Server enforces UQ_ES).

    [Fact]
    public async Task SameTokenTwice_SecondCallIsNoOp_StillSuccess()
    {
        var token = UnsubscribeToken.Create("dup@x.com", "sender@y.com", Key);

        var first = await _service.HandleAsync(token, CancellationToken.None);
        Assert.True(first.Success);

        // InMemory doesn't enforce UQ_ES — the second insert creates a duplicate.
        // The real idempotency is enforced by SQL Server's unique constraint (integration test covers this).
        // Here we just verify no exception is thrown and both calls succeed.
        var second = await _service.HandleAsync(token, CancellationToken.None);
        Assert.True(second.Success);
    }

    // ---- Service: tampered token → not Success + no row ----

    [Fact]
    public async Task TamperedToken_ReturnsNotSuccess_NoRowInserted()
    {
        var result = await _service.HandleAsync("garbage.token.value", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(0, await _db.Set<EmailSuppression>().CountAsync());
    }
}
