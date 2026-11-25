using MediatR;
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Admin;

/// <summary>
/// US-044-01 (TA-4.2a, FR-044-5): set a flag's value — trim, unknown key → FLAG_UNKNOWN, typed validation → FLAG_INVALID,
/// then upsert via IAdminFlagStore. Unknown is checked BEFORE the type (AC-044-3 returns 404 regardless of body).
/// The old value is captured pre-write so the endpoint can audit {old,new} (FR-044-6).
/// </summary>
public sealed class SetFlagCommandHandler : IRequestHandler<SetFlagCommand, SetFlagResult>
{
    private readonly IAdminFlagStore _flags;
    private readonly IFlagsStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="SetFlagCommandHandler"/> class.</summary>
    /// <param name="flags">Admin store that upserts the flag value.</param>
    /// <param name="store">Store used to check, uncached, that the key is a seeded flag.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flags"/>, <paramref name="store"/> or <paramref name="timeProvider"/> is null.</exception>
    public SetFlagCommandHandler(IAdminFlagStore flags, IFlagsStore store, TimeProvider timeProvider)
    {
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<SetFlagResult> Handle(SetFlagCommand request, CancellationToken cancellationToken)
    {
        var value = request.Value.Trim();

        // Direct store read on the write path (bypasses the cache on purpose) — seeded keys only (FR-044-5).
        var rows = await _store.GetAllAsync(cancellationToken);
        if (!rows.Any(r => string.Equals(r.Key, request.Key, StringComparison.Ordinal)))
            throw SetFlagException.ForUnknown(); // 404 before the type check (AC-044-3)

        if (!FlagValueRules.IsValid(request.Key, value))
            throw SetFlagException.ForInvalid(); // 400 — no write, no audit (EC-044-1)

        var old = await _flags.UpsertAsync(request.Key, value, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken)
            ?? throw SetFlagException.ForUnknown(); // row removed between read and write

        return new SetFlagResult(request.Key, old.Value, value);
    }
}
