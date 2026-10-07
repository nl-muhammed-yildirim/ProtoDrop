# T-049-06 — Prove the CQRS convention with a first trivial use case

**Story:** US-049-02 | **Spec:** happy path step 1, AC block 2 (no IQueryable) | **Size:** S (3 files + 1 test)
**Depends on:** T-049-05

---

## Context to read (only these)

- `US-049-02-layer-rules.md` → happy path + second Gherkin block
- `docs/AGENT.md` (repo root docs) → §3 golden rules 7 and 8 only

## Instructions

1. Add a trivial use case in `src/wa.application/UseCases/Sample/Ping.cs`:
   - `public sealed record PingCommand : IRequest<string>;`
   - handler returning `"pong"`.
2. Register MediatR in `wa.api/Program.cs`: `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly of wa.application));`
3. Add a unit test in `tests/wa.application.unit` that publishes `PingCommand` through an in-memory MediatR publisher and asserts `"pong"`.

## Exit check

- [ ] `dotnet build` green; `dotnet test tests/wa.application.unit` passes (now 2 tests)
- [ ] No `DbContext`, no `IQueryable<T>` anywhere in the use case or handler (golden rule 8)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, MediatR 12.5 in wa.application).
Task T-049-06 — add the first trivial use case to prove the CQRS convention.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/US-049-02-layer-rules/US-049-02-layer-rules.md (happy path + second Gherkin block) and docs/AGENT.md §3 rules 7–8 only.
Do exactly:
1. Create src/wa.application/UseCases/Sample/Ping.cs with `public sealed record PingCommand : IRequest<string>;` and a handler class returning "pong".
2. In src/wa.api/Program.cs register MediatR: services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PingCommand).Assembly)); (add the using for wa.application).
3. Add one xUnit test in tests/wa.application.unit that publishes PingCommand via an in-memory MediatR publisher and asserts the result is "pong".
Done when: dotnet build is green and dotnet test tests/wa.application.unit passes with 2 passing tests, no DbContext or IQueryable in any use case code.
Constraints: keep it trivial — this is a convention proof, not a feature (golden rule 8).
```
