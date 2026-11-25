using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-08: the write result — old + new values so the endpoint can audit {old,new} (FR-011-5, FR-044-6).</summary>
/// <param name="Key">The key of the set flag result.</param>
/// <param name="OldValue">The old value of the set flag result.</param>
/// <param name="NewValue">The new value of the set flag result.</param>
public sealed record SetFlagResult(string Key, string OldValue, string NewValue);
