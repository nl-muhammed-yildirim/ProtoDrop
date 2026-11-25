using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-08: the write result — old + new values so the endpoint can audit {old,new} (FR-011-5, FR-044-6).</summary>
public sealed record SetFlagResult(string Key, string OldValue, string NewValue);
