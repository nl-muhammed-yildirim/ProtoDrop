using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.UseCases.Health;
using Xunit;

namespace wa.application.unit.Health;

public class HealthQueryTests
{
    private static ISender BuildSender(IDbHealthProbe db, ISbHealthProbe sb)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(HealthQuery).Assembly));
        services.AddSingleton(db);
        services.AddSingleton(sb);
        services.AddSingleton<ILogger<HealthQueryHandler>>(new StubLogger());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Handle_DatabaseAndServiceBusOk_ReturnsAllOk()
    {
        var sender = BuildSender(new StubDbProbe(), new StubSbProbe(ComponentStatus.Ok));

        var result = await sender.Send(new HealthQuery(), CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal("ok", result.Db);
        Assert.Equal("ok", result.Sb);
    }

    [Fact]
    public async Task Handle_ServiceBusSkipped_ReportsSkipped()
    {
        var sender = BuildSender(new StubDbProbe(), new StubSbProbe(ComponentStatus.Skipped));

        var result = await sender.Send(new HealthQuery(), CancellationToken.None);

        Assert.Equal("skipped", result.Sb);
    }

    [Fact]
    public async Task Handle_DatabaseDown_Fails()
    {
        var sender = BuildSender(new StubDbProbe(throws: true), new StubSbProbe(ComponentStatus.Ok));

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => sender.Send(new HealthQuery(), CancellationToken.None));

        // MediatR ISender wraps handler exceptions; the real cause is preserved.
        var inner = ex is AggregateException ? ex.InnerException : ex;
        Assert.IsType<SqlExceptionLike>(inner);
    }

    private sealed class StubDbProbe(bool throws = false) : IDbHealthProbe
    {
        public Task PingAsync(CancellationToken cancellationToken)
            => throws ? Task.FromException(new SqlExceptionLike()) : Task.CompletedTask;
    }

    private sealed class StubSbProbe(ComponentStatus status) : ISbHealthProbe
    {
        public Task<ComponentStatus> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(status);
    }

    private sealed class SqlExceptionLike : Exception;

    private sealed class StubLogger : ILogger<HealthQueryHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
