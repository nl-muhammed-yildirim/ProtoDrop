namespace wa.domain.Analytics;

/// <summary>A top-N bucket (country code or browser family) with its count.</summary>
/// <param name="Key">The key of the bucket count.</param>
/// <param name="Count">The count of the bucket count.</param>
public sealed record BucketCount(string Key, int Count);
