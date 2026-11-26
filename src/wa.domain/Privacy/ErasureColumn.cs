namespace wa.domain.Privacy;

/// <summary>One entry of the closed erasure list: a raw-PII column and what happens to it when its owner is erased.</summary>
/// <param name="Entity">The entity type that owns the column.</param>
/// <param name="Property">The property name (use <c>nameof</c> so a rename breaks the compile).</param>
/// <param name="Disposition">What the erasure cascade does with the value today.</param>
/// <param name="Rationale">Why the disposition is what it is.</param>
public sealed record ErasureColumn(Type Entity, string Property, ErasureDisposition Disposition, string Rationale);
