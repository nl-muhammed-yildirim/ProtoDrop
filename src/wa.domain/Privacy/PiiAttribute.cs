namespace wa.domain.Privacy;

/// <summary>Marks a property whose value is raw PII under TA-9.4 (email, person/org name, file name, IP). Every marked persisted column must be listed in <c>ErasureScope.AllColumns</c> (F-XCT-005 FR-048-2/4).</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PiiAttribute : Attribute;
