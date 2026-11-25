using System.Text.Json;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-07 (EC-019-4, TA-7.5 "Stripe is truth"): Stripe's CURRENT view of one subscription.
/// Parsed identically from a webhook event's data.object and from GET v1/subscriptions/{id}.
/// </summary>
/// <param name="Id">Stripe subscription id.</param>
/// <param name="Status">Stripe subscription status.</param>
/// <param name="CustomerId">Stripe customer id; null when absent.</param>
/// <param name="PriceId">Stripe Price id of the first subscription item; null when absent.</param>
/// <param name="CurrentPeriodEndUtc">End of the current paid period; null when absent.</param>
/// <param name="MetadataUserId">User id stored in the subscription metadata at checkout; null when absent.</param>
public sealed record StripeSubscriptionSnapshot(
    string Id,
    string Status,
    string? CustomerId,
    string? PriceId,
    DateTime? CurrentPeriodEndUtc,
    Guid? MetadataUserId)
{
    /// <summary>Parses a Stripe subscription object (from a webhook payload or the REST API).</summary>
    /// <param name="obj">The subscription JSON object.</param>
    /// <returns>The snapshot, or <see langword="null"/> when the id or status is missing or the element is not an object.</returns>
    public static StripeSubscriptionSnapshot? FromJson(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        var subId = GetString(obj, "id");
        var status = GetString(obj, "status");
        if (string.IsNullOrEmpty(subId) || string.IsNullOrEmpty(status))
            return null;

        JsonElement? firstItem = null;
        if (obj.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object
            && items.TryGetProperty("data", out var itemData) && itemData.ValueKind == JsonValueKind.Array
            && itemData.GetArrayLength() > 0)
            firstItem = itemData[0];

        string? priceId = null;
        if (firstItem is { ValueKind: JsonValueKind.Object } item
            && item.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object)
            priceId = GetString(price, "id");

        var periodEnd = ReadUnix(obj, "current_period_end")
            ?? (firstItem is { ValueKind: JsonValueKind.Object } item2 ? ReadUnix(item2, "current_period_end") : null);

        Guid? metadataUserId = null;
        if (obj.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            && Guid.TryParse(GetString(metadata, "userId"), out var parsed))
            metadataUserId = parsed;

        return new StripeSubscriptionSnapshot(subId, status, GetString(obj, "customer"), priceId, periodEnd, metadataUserId);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? ReadUnix(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
            : null;
}
