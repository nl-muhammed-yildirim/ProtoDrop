# T-049-05 — Add the canonical folder homes with one-line READMEs

**Story:** US-049-02 | **Spec:** happy path (folders), TA-2.3/TA-2.4 | **Size:** S (docs only)
**Depends on:** T-049-04

---

## Context to read (only these)

- `US-049-02-layer-rules.md` → happy path + Technical notes
- `docs/AGENT.md` (repo root docs) → §3 golden rules 7 and 8 only

## Instructions

1. Create the canonical folders with a one-line README in each:
   - `src/wa.application/UseCases/README.md` — "One MediatR `IRequest<T>` + handler per use case, CQRS naming `{Verb}{Noun}Command` / `{Verb}{Noun}Query` (TA-2.4)."
   - `src/wa.application/Ports/README.md` — "Port interfaces only; adapters live in wa.infrastructure (TA-0.2 rule 7)."
   - `src/wa.infrastructure/Persistence/README.md` — "`WaDbContext` + `IEntityTypeConfiguration<T>` only — EF never leaks into core (TA-2.3)."

## Exit check

- [ ] All three folders exist with their README lines
- [ ] `dotnet build` still green

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, clean/onion).
Task T-049-05 — create the canonical folder homes.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/US-049-02-layer-rules/US-049-02-layer-rules.md (happy path + Technical notes).
Do exactly:
1. Create src/wa.application/UseCases/README.md containing exactly: One MediatR IRequest<T> + handler per use case, CQRS naming {Verb}{Noun}Command / {Verb}{Noun}Query (TA-2.4).
2. Create src/wa.application/Ports/README.md containing exactly: Port interfaces only; adapters live in wa.infrastructure (TA-0.2 rule 7).
3. Create src/wa.infrastructure/Persistence/README.md containing exactly: WaDbContext + IEntityTypeConfiguration<T> only — EF never leaks into core (TA-2.3).
Done when: the three folders are committed with those READMEs and dotnet build is still green.
Constraints: no C# code changes in this task.
```
