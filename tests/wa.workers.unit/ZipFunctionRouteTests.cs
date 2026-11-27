using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using wa.application.UseCases.Transfers;
using wa.workers;
using Xunit;

namespace wa.workers.unit;

/// <summary>
/// CHORE-zip-route-prefix: the isolated worker binds route values by parameter name, so the trigger route and the
/// <c>Run</c> signature must agree; the route must also match the contract the API caller uses.
/// </summary>
public class ZipFunctionRouteTests
{
    private static HttpTriggerAttribute Trigger()
    {
        var run = typeof(ZipFunction).GetMethod(nameof(ZipFunction.Run))!;
        return run.GetParameters()
            .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
            .Single(a => a is not null)!;
    }

    [Fact]
    public void Run_TriggerRoute_EqualsSharedContract()
    {
        Assert.Equal(ZipHandOff.Route, Trigger().Route);
    }

    [Fact]
    public void Run_EveryRoutePlaceholder_HasMatchingParameterName()
    {
        var run = typeof(ZipFunction).GetMethod(nameof(ZipFunction.Run))!;
        var parameterNames = run.GetParameters().Select(p => p.Name).ToList();
        var placeholders = Regex.Matches(Trigger().Route!, @"\{(\w+)(:[^}]*)?\}").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(placeholders);
        foreach (var name in placeholders)
            Assert.Contains(name, parameterNames, StringComparer.Ordinal);
    }

    [Fact]
    public void Run_TriggerIsPostOnly()
    {
        var methods = Trigger().Methods;

        Assert.NotNull(methods);
        Assert.Single(methods!);
        Assert.Equal("post", methods![0], ignoreCase: true);
    }
}
