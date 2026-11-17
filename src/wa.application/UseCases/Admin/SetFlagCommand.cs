using MediatR;
using System.Text.Json;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): PUT /api/v1/admin/flags/{key} — SuperAdmin-only flag value write.
/// The value takes effect within 30 s via the EXISTING FlagsCache TTL (TA-3.4) — no invalidation code here.
/// </summary>
public sealed record SetFlagCommand(string Key, string Value) : IRequest<SetFlagResult>;

/// <summary>T-011-08: the write result — old + new values so the endpoint can audit {old,new} (FR-011-5).</summary>
public sealed record SetFlagResult(string Key, string OldValue, string NewValue);

/// <summary>
/// T-011-08 (D-2): the single use-case exception for the set-flag write — house style: private ctor + static factories.
/// ForInvalidJson → 400 VALIDATION with message "Invalid JSON" (EC-011-4: old row untouched, no audit row).
/// ForNotFound → 404 NOT_FOUND (unknown key). No new closed-list member (TA-4.1.3 stays at 18).
/// </summary>
public sealed class SetFlagException : Exception
{
    private SetFlagException(string code, string message) : base(message) => Code = code;

    /// <summary>A closed TA-4.1.3 code — the endpoint maps it to its canonical status + title.</summary>
    public string Code { get; }

    /// <summary>Bad JSON body → 400 VALIDATION with message "Invalid JSON" (EC-011-4: thrown BEFORE any DB call).</summary>
    public static SetFlagException ForInvalidJson() =>
        new(ProblemCodes.Validation, "Invalid JSON");

    /// <summary>No row for the key → 404 NOT_FOUND (title from the closed table — never "flag not found").</summary>
    public static SetFlagException ForNotFound() =>
        new(ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound));
}

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): set a flag's value — validate JSON FIRST (cheap, no DB), then upsert via IAdminFlagStore.
/// The old value is captured pre-write so the endpoint can audit {old,new} (FR-011-5).
/// </summary>
public sealed class SetFlagCommandHandler : IRequestHandler<SetFlagCommand, SetFlagResult>
{
    private readonly IAdminFlagStore _flags;

    public SetFlagCommandHandler(IAdminFlagStore flags)
    {
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
    }

    public async Task<SetFlagResult> Handle(SetFlagCommand request, CancellationToken cancellationToken)
    {
        // 1. JSON-shape validation FIRST — bad JSON → ForInvalidJson BEFORE any DB call (EC-011-4: old row untouched).
        try
        {
            using var doc = JsonDocument.Parse(request.Value);
        }
        catch (JsonException)
        {
            throw SetFlagException.ForInvalidJson(); // 400 VALIDATION "Invalid JSON" — no DB call, no audit row
        }

        // 2. Upsert via IAdminFlagStore — returns the OLD row (pre-update) or null when no row for that key.
        var old = await _flags.UpsertAsync(request.Key, request.Value, DateTime.UtcNow, cancellationToken);
        if (old is null)
            throw SetFlagException.ForNotFound(); // unknown key → 404 NOT_FOUND at the endpoint

        // 3. Return old + new so the endpoint audits {old,new} (FR-011-5).
        return new SetFlagResult(request.Key, old.Value, request.Value);
    }
}
