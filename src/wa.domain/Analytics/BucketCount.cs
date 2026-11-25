namespace wa.domain.Analytics;

/// <summary>A top-N bucket (country code or browser family) with the number of downloads that fell into it.</summary>
/// <param name="Key">Normalized bucket key: an upper-case ISO country code (or <c>??</c>) or a browser family name.</param>
/// <param name="Count">Number of download events merged into the bucket.</param>
public sealed record BucketCount(string Key, int Count);
