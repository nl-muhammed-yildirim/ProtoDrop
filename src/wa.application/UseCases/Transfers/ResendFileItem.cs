using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>One file row in the re-send result (endpoint 16's JSON + T-010-03's banner copy).</summary>
/// <param name="FileId">Id of the new file row created for the re-sent draft.</param>
/// <param name="Name">Original file name.</param>
public sealed record ResendFileItem(Guid FileId, string Name);
