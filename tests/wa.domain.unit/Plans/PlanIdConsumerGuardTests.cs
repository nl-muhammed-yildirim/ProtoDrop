using System.Text.RegularExpressions;

namespace wa.domain.unit.Plans;

/// <summary>US-020-01 (AC-020-4): limits and features are read through IPlanContextAccessor; AppUser.PlanId is touched only by plan identity / plan write paths.</summary>
public class PlanIdConsumerGuardTests
{
    private static readonly Regex PlanIdAccess = new(@"\.PlanId\b", RegexOptions.Compiled);

    private static readonly string[] ScanRoots = ["src/wa.application", "src/wa.api", "src/wa.workers"];

    private static readonly string[] AllowList =
    [
        "src/wa.application/UseCases/Admin/SetUserPlanCommandHandler.cs", // admin plan write + audit
        "src/wa.application/UseCases/Auth/LoginHandler.cs", // LoginResult.PlanCode identity
        "src/wa.application/UseCases/Auth/SignupHandler.cs", // signup on a known email = login: SignupResult.PlanCode identity (CHORE-staging-blockers)
        "src/wa.application/UseCases/Auth/GetProfileHandler.cs", // profile DTO plan code
        "src/wa.application/UseCases/Auth/UpdateProfileHandler.cs", // profile DTO plan code
        "src/wa.application/UseCases/Auth/RedeemMagicLinkHandler.cs", // login result plan code
        "src/wa.application/UseCases/Billing/CreateCheckoutSessionHandler.cs" // already-on-plan check (identity, not a limit)
    ];

    [Fact]
    public void OnlyPlanIdentityAndWritePaths_ReadPlanId()
    {
        var root = FindRepoRoot();
        var hits = new List<string>();

        foreach (var scanRoot in ScanRoots)
        {
            var dir = Path.Combine(root, scanRoot);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Split('/').Any(s => s is "bin" or "obj" or "Migrations")) continue;
                if (AllowList.Contains(relative)) continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (PlanIdAccess.IsMatch(lines[i])) hits.Add($"{relative}:{i + 1}");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "AppUser.PlanId read outside the allow-list — resolve limits/features via IPlanContextAccessor (AC-020-4):\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }
}
