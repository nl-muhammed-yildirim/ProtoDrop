using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-04: request result — 202 Accepted with no body fields.</summary>
public sealed record RequestMagicLinkResult();
