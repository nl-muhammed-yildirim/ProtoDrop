namespace wa.domain.Analytics;

/// <summary>A top-N bucket (country code or browser family) with its count.</summary>
public sealed record BucketCount(string Key, int Count);
