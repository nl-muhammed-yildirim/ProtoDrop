using MediatR;
using Microsoft.Extensions.DependencyInjection;
using wa.application.UseCases.Sample;
using Xunit;

namespace wa.application.unit.Sample;

public class PingCommandTests
{
    [Fact]
    public async Task PingCommand_ReturnsPong()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PingCommand).Assembly));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new PingCommand(), CancellationToken.None);

        Assert.Equal("pong", result);
    }
}
