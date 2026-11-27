using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using wa.api.Middleware;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.api.integration;

/// <summary>
/// CHORE-staging-blockers r3: the session epoch read runs before GlobalErrorMiddleware, so a failing store must not surface as a bare 500 and
/// must never authenticate the request. Pure middleware test (no containers).
/// </summary>
public class SessionMiddlewareFailClosedTests
{
    private static readonly byte[] Key = System.Text.Encoding.UTF8.GetBytes("unit-test-session-key");

    private static (SessionMiddleware Middleware, string Token, Func<bool> NextCalled) Build()
    {
        var tokens = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var called = false;
        var middleware = new SessionMiddleware(_ => { called = true; return Task.CompletedTask; }, tokens, TimeProvider.System, NullLogger<SessionMiddleware>.Instance);
        return (middleware, tokens.Mint(Guid.NewGuid()), () => called);
    }

    private static DefaultHttpContext ContextWith(string token)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"wa.session={token}";
        return ctx;
    }

    [Fact]
    public async Task EpochReadThrows_RequestContinuesAsGuest_NoSessionUser_NoRenewalCookie()
    {
        var (middleware, token, nextCalled) = Build();
        var ctx = ContextWith(token);

        await middleware.InvokeAsync(ctx, new ThrowingUsers(new InvalidOperationException("sql down")));

        Assert.True(nextCalled()); // the endpoint still answers (Problem+JSON via GlobalErrorMiddleware)
        Assert.False(ctx.Items.ContainsKey(SessionMiddleware.SessionUserIdKey)); // fail closed: never authenticated
        Assert.False(ctx.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task ClientAborts_RequestEndsQuietly()
    {
        var (middleware, token, nextCalled) = Build();
        using var cts = new CancellationTokenSource();
        var ctx = ContextWith(token);
        ctx.RequestAborted = cts.Token;
        await cts.CancelAsync();

        await middleware.InvokeAsync(ctx, new ThrowingUsers(new OperationCanceledException(cts.Token)));

        Assert.False(nextCalled());
        Assert.False(ctx.Items.ContainsKey(SessionMiddleware.SessionUserIdKey));
    }

    private sealed class ThrowingUsers(Exception toThrow) : IAppUserStore
    {
        public Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct) => throw toThrow;

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateAsync(AppUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }
}
