using System.Text.RegularExpressions;

namespace wa.domain.unit.Plans;

/// <summary>US-020-04 (FR-020-5, AC-020-4): feature availability is read only through IPlanContextAccessor → PlanContext.Features, and no code branches on the plan code to gate a feature.</summary>
public class PlanFeaturesConsumerGuardTests
{
    private static readonly Regex FeaturesProviderUse = new(@"\bI?FeaturesProvider\b", RegexOptions.Compiled);

    private static readonly Regex PlanCodeComparison = new(
        @"(==|!=)\s*(Plan\.DefaultCode|(ILimitsProvider\.)?GuestPlanCode)\b|\b(Plan\.DefaultCode|GuestPlanCode)\s*(==|!=)",
        RegexOptions.Compiled);

    private static readonly string[] ScanRoots = ["src"];

    private static readonly string[] FeaturesProviderAllowList =
    [
        "src/wa.application/Limits/FeaturesProvider.cs", // definition
        "src/wa.application/Limits/PlanContext.cs", // the single consumer of the viewer's plan
        "src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs", // US-021-01: anonymous endpoint, features of the transfer SENDER's plan (no viewer PlanContext exists)
        "src/wa.application/UseCases/Transfers/SenderBranding.cs", // US-021-02: sender's plan gates email branding (no viewer PlanContext in f-email)
        "src/wa.application/UseCases/Transfers/SendNotificationEmails.cs", // US-021-02: f-email handler passes the sender's features to SenderBranding
        "src/wa.workers/WorkerServices.cs", // US-021-02: worker DI wiring for the sender-plan features provider
        "src/wa.api/Program.cs" // DI wiring
    ];

    private static readonly string[] PlanCodeComparisonAllowList =
    [
        "src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs", // checkout target / already-paid identity
        "src/wa.infrastructure/Persistence/BillingGraceStore.cs" // downgrade-to-free row lookup
    ];

    [Fact]
    public void OnlyPlanContext_ResolvesFeatures()
    {
        AssertNoHits(FeaturesProviderUse, FeaturesProviderAllowList,
            "FeaturesProvider used outside the allow-list — read features via IPlanContextAccessor → PlanContext.Features (FR-020-5):");
    }

    [Fact]
    public void NoPlanCodeComparisons_OutsideIdentityPaths()
    {
        AssertNoHits(PlanCodeComparison, PlanCodeComparisonAllowList,
            "Plan-code comparison outside the identity allow-list — gate features on PlanContext.Features, never on the plan code (AC-020-4):");
    }

    private static void AssertNoHits(Regex pattern, string[] allowList, string message)
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
                if (allowList.Contains(relative)) continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (pattern.IsMatch(lines[i])) hits.Add($"{relative}:{i + 1}");
                }
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
