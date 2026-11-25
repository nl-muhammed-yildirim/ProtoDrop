using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/draft request — the full staged-file selection (TA-4.2#1).</summary>
/// <param name="Files">Name, size and content type of every file the sender selected.</param>
public sealed record CreateDraftCommand(IReadOnlyList<CreateDraftFile> Files) : IRequest<CreateDraftResult>;
