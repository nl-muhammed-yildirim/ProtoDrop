namespace wa.domain;

public class Plan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LimitsJson { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
