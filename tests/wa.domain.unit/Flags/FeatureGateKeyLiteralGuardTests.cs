using System.Text.RegularExpressions;

namespace wa.domain.unit.Flags;

/// <summary>US-044-02 (D2): the six phase-gate key literals live only in FeatureGates.cs and the seed config — consumers reference the constants, so the gate call is the only difference between phases.</summary>
public class FeatureGateKeyLiteralGuardTests
{
    private static readonly Regex GateKey = new("\"feature\\.(search|dataExport|emailSettings|ads|scheduling|analytics)\"", RegexOptions.Compiled);

    private static readonly string[] AllowList =
    [
        "src/wa.application/Limits/FeatureGates.cs",
        "src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs" // seed rows
    ];

    [Fact]
    public void GateKeyLiterals_OnlyInFeatureGatesAndSeed()
    {
        var root = FindRepoRoot();
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Split('/').Any(s => s is "bin" or "obj" or "Migrations")) continue;
            if (AllowList.Contains(relative)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (GateKey.IsMatch(lines[i])) hits.Add($"{relative}:{i + 1}");
            }
        }

        Assert.True(hits.Count == 0, "Gate key literal outside FeatureGates.cs (use the constant):\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }
}
