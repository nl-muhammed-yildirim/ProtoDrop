using System.Text.RegularExpressions;

namespace wa.domain.unit.Plans;

/// <summary>T-031-03 (US-018-03: nothing hardcoded, never computed in app code): the PlanPrice read model is the only backend consumer of Plan.PriceJson. Allow-list: the read model, the validator, and the column mapping/seed.</summary>
public class PriceJsonConsumerGuardTests
{
    private static readonly Regex MemberAccess = new(@"\.PriceJson\b", RegexOptions.Compiled);

    private static readonly string[] AllowList =
    [
        "src/wa.domain/PlanPrice.cs", // the read model
        "src/wa.domain/PlanDefinition.cs", // validate-then-write (EC-018-2)
        "src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs" // column mapping + seed
    ];

    [Fact]
    public void OnlyReadModelValidatorAndSeed_ReadPlanPriceJson()
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

        Assert.True(hits.Count == 0, "Plan.PriceJson read outside the read model (use PlanPrice.Read):\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }
}
