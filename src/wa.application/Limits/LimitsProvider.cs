using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using wa.domain;

namespace wa.application.Limits;

/// <summary>
/// TA-3.4 <c>ILimitsProvider</c> implementation: resolves <see
/// cref="LimitsRecord"/> per request ? <c>Plan.LimitsJson</c> is the
/// base and a matching <c>limits.<plan>.*</c> <c>FeatureFlag</c>
/// value OVERRIDES the ONE corresponding field. Plan and flag reads go
/// through the TA-3.4 caches (30 s TTL, Part 2 technical constant), so
/// DB reads stay on the application/infrastructure boundary (TA-0.2 rule
/// 7: no <c>DbContext</c> in domain/use cases).
/// </summary>
public sealed class LimitsProvider : ILimitsProvider
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Limit flag segments ? <see cref="LimitsRecord"/> member names.
    /// Closed list mirroring the seed rows and the Part-2 constants per
    /// TA-13.2; unknown segments (e.g. <c>feature.*</c> gates) are ignored
    /// here and belong to the flag service (T-065).
    /// </summary>
    static readonly IReadOnlyDictionary<string, string> FieldByFlagSegment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["maxTransferSize"] = nameof(LimitsRecord.MaxTransferSize),
        ["maxSingleFile"] = nameof(LimitsRecord.MaxSingleFile),
        ["maxZipSize"] = nameof(LimitsRecord.MaxZipSize),
        ["retentionDays"] = nameof(LimitsRecord.RetentionDays),
        ["graceDays"] = nameof(LimitsRecord.GraceDays),
        ["maxDownloads"] = nameof(LimitsRecord.MaxDownloads),
        ["maxEmails"] = nameof(LimitsRecord.MaxEmails),
        ["storageQuota"] = nameof(LimitsRecord.StorageQuota),
        ["activeTransfersMax"] = nameof(LimitsRecord.ActiveTransfersMax),
        ["scheduling"] = nameof(LimitsRecord.Scheduling),
        ["branding"] = nameof(LimitsRecord.Branding),
        ["analytics"] = nameof(LimitsRecord.Analytics),
        ["ads"] = nameof(LimitsRecord.Ads),
        ["ssoScim"] = nameof(LimitsRecord.SsoScim),
    };

    /// <summary>Prefix of a plan-limits flag key: <c>limits.{planCode}.</c></summary>
    const string LimitPrefix = "limits.";

    static readonly PropertyInfo[] Members =
        typeof(LimitsRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    readonly PlansCache _plans;
    readonly FlagsCache _flags;

    public LimitsProvider(PlansCache plans, FlagsCache flags)
    {
        _plans = plans;
        _flags = flags;
    }

    public LimitsRecord Resolve(string planCode)
        => ResolveAsync(planCode).GetAwaiter().GetResult();

    public async Task<LimitsRecord> ResolveAsync(string planCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planCode);

        var rows = await _plans.GetPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = rows.FirstOrDefault(p => string.Equals(p.Code, planCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Plan '{planCode}' not found in the plans cache.");

        var record = JsonSerializer.Deserialize<LimitsRecord>(plan.LimitsJson, JsonOptions)
            ?? throw new InvalidOperationException($"Plan.LimitsJson for plan '{planCode}' did not deserialize.");

        // Flag override: each limits.{plan}.* flag with a known field name
        // replaces ONLY that field's value; every other flag key is skipped
        // (one key per Part-2 constant per plan per TA-13.2).
        var flags = await _flags.GetFlagsAsync(cancellationToken).ConfigureAwait(false);
        var prefix = LimitPrefix + planCode.ToLowerInvariant() + ".";
        for (int i = 0; i < flags.Count; i++)
        {
            var key = flags[i].Key;
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var segment = key[prefix.Length..];
            if (!FieldByFlagSegment.TryGetValue(segment, out var memberName))
            {
                continue;
            }

            var prop = FindMember(memberName);
            if (prop is null)
            {
                continue;
            }

            var value = JsonValue(flags[i].Value, prop.PropertyType);
            if (value is long li && prop.PropertyType == typeof(int))
            {
                value = (int)li;
            }

            prop.SetValue(record, value);
        }

        return record;
    }

    static PropertyInfo? FindMember(string name)
        => Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parse a flag's JSON scalar into the target property type (long / int / bool).</summary>
    static object? JsonValue(string json, Type targetType)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { });
        var el = doc.RootElement;

        return targetType switch
        {
            Type t when t == typeof(long) => el.TryGetInt64(out var l) ? l : Convert.ToInt64(el, CultureInfo.InvariantCulture),
            Type t when t == typeof(int) => el.TryGetInt32(out var i) ? i : Convert.ToInt32(el, CultureInfo.InvariantCulture),
            Type t when t == typeof(bool) => el.GetBoolean(),
            _ => throw new JsonException($"Unsupported target type {targetType} for flag value '{json}'."),
        };
    }
}