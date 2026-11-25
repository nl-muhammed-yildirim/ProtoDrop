using System.Text.RegularExpressions;

namespace wa.domain.unit.Plans;

/// <summary>T-031-02 (US-018-02 AC block 1): the limits resolver is the only backend consumer of Plan.LimitsJson — no other code path reads plan limit numbers. Allow-list: the resolver, the validator, and the column mapping/seed.</summary>
public class LimitsJsonConsumerGuardTests
{
    private static readonly Regex MemberAccess = new(@"\.LimitsJson\b", RegexOptions.Compiled);

    private static readonly string[] AllowList =
    [
        "src/wa.application/Limits/LimitsProvider.cs", // the resolver
        "src/wa.application/UseCases/Admin/GetFlagsQueryHandler.cs", // US-044-01: admin Flags view shows each limit's plan seed default (read through PlansCache)
        "src/wa.domain/PlanDefinition.cs", // validate-then-write (EC-018-2)
        "src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs" // column mapping + seed
    ];

    [Fact]
    public void OnlyResolverValidatorAndSeed_ReadPlanLimitsJson()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root, "src");
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Any(s => s is "bin" or "obj" or "Migrations")) continue;
            if (AllowList.Contains(relative)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (MemberAccess.IsMatch(lines[i])) hits.Add($"{relative}:{i + 1}");
            }
        }

        Assert.True(hits.Count == 0, "Plan.LimitsJson read outside the resolver (use ILimitsProvider.Resolve):\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }
}
