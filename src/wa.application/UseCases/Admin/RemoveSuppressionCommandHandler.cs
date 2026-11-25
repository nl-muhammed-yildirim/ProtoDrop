using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-09 (TA-4.2#24): remove a suppression row — null pass-through (endpoint maps to 404).</summary>
public sealed class RemoveSuppressionCommandHandler : IRequestHandler<RemoveSuppressionCommand, EmailSuppression?>
{
    private readonly IAdminSuppressionStore _store;

    /// <summary>Initializes a new instance of the <see cref="RemoveSuppressionCommandHandler"/> class.</summary>
    /// <param name="store">The store.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public RemoveSuppressionCommandHandler(IAdminSuppressionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<EmailSuppression?> Handle(RemoveSuppressionCommand request, CancellationToken cancellationToken)
    {
        // Null = unknown id (endpoint maps to 404 NOT_FOUND — mirrors T-011-05/07). No exception type needed (single case).
        return await _store.RemoveByIdAsync(request.Id, cancellationToken);
    }
}
