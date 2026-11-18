using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 / US-044-01 (TA-4.2#23, FR-044-5): PUT /api/v1/admin/flags/{key} — SuperAdmin-only flag value write.
/// The value takes effect within 30 s via the EXISTING FlagsCache TTL (TA-3.4) — no invalidation code here.
/// </summary>
public sealed record SetFlagCommand(string Key, string Value) : IRequest<SetFlagResult>;

/// <summary>T-011-08: the write result — old + new values so the endpoint can audit {old,new} (FR-011-5, FR-044-6).</summary>
public sealed record SetFlagResult(string Key, string OldValue, string NewValue);

/// <summary>
/// T-011-08 / US-044-01: the single use-case exception for the set-flag write — house style: private ctor + static factories.
/// ForInvalid → 400 FLAG_INVALID (value not valid for the key's type; old value stays live, no audit row).
/// ForUnknown → 404 FLAG_UNKNOWN (key is not a seeded flag). Both codes are in the closed TA-4.1.3 list (US-044-01).
/// </summary>
public sealed class SetFlagException : Exception
{
    private SetFlagException(string code, string message) : base(message) => Code = code;

    /// <summary>A closed TA-4.1.3 code — the endpoint maps it to its canonical status + title.</summary>
    public string Code { get; }

    /// <summary>Value fails the key's typed rules → 400 FLAG_INVALID (EC-044-1, EC-044-5).</summary>
    public static SetFlagException ForInvalid() =>
        new(ProblemCodes.FlagInvalid, ProblemCodes.TitleOf(ProblemCodes.FlagInvalid));

    /// <summary>No seeded row for the key → 404 FLAG_UNKNOWN (AC-044-3).</summary>
    public static SetFlagException ForUnknown() =>
        new(ProblemCodes.FlagUnknown, ProblemCodes.TitleOf(ProblemCodes.FlagUnknown));
}

/// <summary>
/// US-044-01 (TA-4.2a, FR-044-5): set a flag's value — trim, unknown key → FLAG_UNKNOWN, typed validation → FLAG_INVALID,
/// then upsert via IAdminFlagStore. Unknown is checked BEFORE the type (AC-044-3 returns 404 regardless of body).
/// The old value is captured pre-write so the endpoint can audit {old,new} (FR-044-6).
/// </summary>
public sealed class SetFlagCommandHandler : IRequestHandler<SetFlagCommand, SetFlagResult>
{
    private readonly IAdminFlagStore _flags;
    private readonly IFlagsStore _store;

    public SetFlagCommandHandler(IAdminFlagStore flags, IFlagsStore store)
    {
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<SetFlagResult> Handle(SetFlagCommand request, CancellationToken cancellationToken)
    {
        var value = request.Value.Trim();

        // Direct store read on the write path (bypasses the cache on purpose) — seeded keys only (FR-044-5).
        var rows = await _store.GetAllAsync(cancellationToken);
        if (!rows.Any(r => string.Equals(r.Key, request.Key, StringComparison.Ordinal)))
            throw SetFlagException.ForUnknown(); // 404 before the type check (AC-044-3)

        if (!FlagValueRules.IsValid(request.Key, value))
            throw SetFlagException.ForInvalid(); // 400 — no write, no audit (EC-044-1)

        var old = await _flags.UpsertAsync(request.Key, value, DateTime.UtcNow, cancellationToken)
            ?? throw SetFlagException.ForUnknown(); // row removed between read and write

        return new SetFlagResult(request.Key, old.Value, value);
    }
}
