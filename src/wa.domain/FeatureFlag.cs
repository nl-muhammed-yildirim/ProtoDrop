namespace wa.domain;

public class FeatureFlag
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
