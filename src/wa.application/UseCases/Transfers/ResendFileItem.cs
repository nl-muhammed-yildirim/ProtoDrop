using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>One file row in the re-send result (endpoint 16's JSON + T-010-03's banner copy).</summary>
public sealed record ResendFileItem(Guid FileId, string Name);
