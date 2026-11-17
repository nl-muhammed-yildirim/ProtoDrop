using System.Text.RegularExpressions;

namespace wa.domain.unit.Plans;

/// <summary>T-031-01 (FR-018-2, US-018-01 happy path 3): backend code never branches on / names a plan code literal; the default plan comes from Plan.DefaultCode and plan data comes from the DB. Allow-list: the constant itself and the seed rows.</summary>
public class PlanCodeLiteralGuardTests
{
    private static readonly Regex PlanLiteral = new("\"(free|pro|business)\"", RegexOptions.Compiled);

    private static readonly string[] AllowList =
    [
        "src/wa.domain/Plan.cs",
        "src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs"
    ];

    [Fact]
    public void NoPlanCodeLiterals_InBackendSource()
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
                if (PlanLiteral.IsMatch(lines[i])) hits.Add($"{relative}:{i + 1}");
        }

        Assert.True(hits.Count == 0, "Plan code literals found (use Plan.DefaultCode or read plan data):\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }
}
