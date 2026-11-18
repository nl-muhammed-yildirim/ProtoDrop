using System.Text.RegularExpressions;

namespace wa.domain.unit.Plans;

/// <summary>T-031-05 (US-018-05 happy path 2, FR-018-2): FeaturesProvider is the only backend reader of Plan.FeaturesJson and of the plan-feature global gate keys.</summary>
public class FeaturesJsonConsumerGuardTests
{
    private static readonly Regex FeaturesJsonAccess = new(@"\.FeaturesJson\b", RegexOptions.Compiled);
    private static readonly Regex GateKeyLiteral = new("\"feature\\.(ads|scheduling|analytics|branding|ssoScim)\"", RegexOptions.Compiled);

    private static readonly string[] FeaturesJsonAllowList =
    [
        "src/wa.application/Limits/FeaturesProvider.cs", // the resolver
        "src/wa.domain/PlanDefinition.cs", // validate-then-write (EC-018-2)
        "src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs" // column mapping + seed
    ];

    private static readonly string[] GateKeyAllowList =
    [
        "src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs", // the seed
        "src/wa.application/Limits/FeatureGates.cs" // US-044-02 phase gates: same seeded rows, absent = off (F-XCT-001)
    ];

    [Fact]
    public void OnlyResolverValidatorAndSeed_ReadPlanFeaturesJson() =>
        AssertNoHits(FeaturesJsonAccess, FeaturesJsonAllowList, "Plan.FeaturesJson read outside FeaturesProvider (use IFeaturesProvider.Resolve):");

    [Fact]
    public void OnlySeed_SpellsPlanFeatureGateKeys() =>
        AssertNoHits(GateKeyLiteral, GateKeyAllowList, "Plan-feature gate key literal outside the seed (FeaturesProvider derives keys from PlanFeatures):");

    private static void AssertNoHits(Regex pattern, string[] allowList, string message)
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root, "src");
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Any(s => s is "bin" or "obj" or "Migrations")) continue;
            if (allowList.Contains(relative)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (pattern.IsMatch(lines[i])) hits.Add($"{relative}:{i + 1}");
            }
        }

        Assert.True(hits.Count == 0, message + "\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }
}
