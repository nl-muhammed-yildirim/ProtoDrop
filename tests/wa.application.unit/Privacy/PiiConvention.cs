using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using wa.domain.Privacy;
using wa.infrastructure.Persistence;

namespace wa.application.unit.Privacy;

/// <summary>
/// F-XCT-005 (US-048-02/03): the PII naming convention and the closed-list check as pure functions, so the same logic runs on the real
/// EF model and on fixture types (red on an unmarked fixture, green once marked).
/// </summary>
internal static partial class PiiConvention
{
    private static readonly HashSet<string> PiiWords = ["email", "mail", "name", "ip", "address"];

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex WordRegex();

    /// <summary>True when a camelCase word of the property name is email, mail, name, ip or address (FR-048-4 plus address).</summary>
    internal static bool IsPiiLookingName(string name) =>
        WordRegex().Matches(name).Any(m => PiiWords.Contains(m.Value.ToLowerInvariant()));

    /// <summary>Violations of the naming rule over the given string columns; empty means the convention holds.</summary>
    internal static IReadOnlyList<string> NamingViolations(IEnumerable<PropertyInfo> stringColumns)
    {
        var violations = new List<string>();
        foreach (var p in stringColumns)
        {
            var id = $"{p.DeclaringType!.Name}.{p.Name}";
            var pii = p.GetCustomAttribute<PiiAttribute>() is not null;
            var notPii = p.GetCustomAttribute<NotPiiAttribute>();
            if (pii && notPii is not null) violations.Add($"{id}: has both [Pii] and [NotPii] - pick one (US-048-02)");
            else if (notPii is not null && string.IsNullOrWhiteSpace(notPii.Reason)) violations.Add($"{id}: [NotPii] needs a non-blank reason (US-048-02)");
            else if (!pii && notPii is null && IsPiiLookingName(p.Name))
                violations.Add($"{id}: PII-looking string column without a marker - mark it [Pii] (and add it to ErasureScope.AllColumns) or [NotPii(\"reason\")] (US-048-02)");
        }
        return violations;
    }

    /// <summary>Every mapped <see cref="string"/> property of the real EF model, as CLR properties.</summary>
    internal static IReadOnlyList<PropertyInfo> ModelStringColumns()
    {
        var options = new DbContextOptionsBuilder<WaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new WaDbContext(options);
        return db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && p.PropertyInfo is not null)
            .Select(p => p.PropertyInfo!)
            .DistinctBy(p => (p.DeclaringType, p.Name))
            .ToList();
    }
}
