using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-08: the write result — old + new values so the endpoint can audit {old,new} (FR-011-5, FR-044-6).</summary>
/// <param name="Key">The flag key that was written.</param>
/// <param name="OldValue">Value before the write.</param>
/// <param name="NewValue">Value after the write (trimmed).</param>
public sealed record SetFlagResult(string Key, string OldValue, string NewValue);
