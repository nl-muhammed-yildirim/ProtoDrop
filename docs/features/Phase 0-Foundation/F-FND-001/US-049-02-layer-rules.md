# US-049-02 — Know exactly where new code goes

**Feature:** F-FND-001 — Solution & Project Scaffold | **Status:** done (T-001, 2026-08-31)

---

**Story:** As a developer adding any feature, I want the project layout to encode the architecture rules, so that I never have to decide which layer owns a type or add a forbidden dependency.
**Actor:** Developer (human or AI session), working on any M1+ task.
**Goal:** The clean (onion) rule TA-2.3 is enforced by structure: each layer has exactly one home per kind of code.

## Preconditions

- Solution scaffolded per F-FND-001 (T-001).
- Golden rules in AGENT.md §3 / TA-0.2 known.

## Happy path

1. A new use case is added as `IRequest<T>` + handler in `wa.application/UseCases/{Feature}/` (TA-2.4).
2. Its port interface goes to `wa.application/Ports`; its adapter to `wa.infrastructure`.
3. The domain entity stays a POCO in `wa.domain` with zero external references.
4. The build passes; no layer violation is possible because the reference graph only allows TA-2.2 edges.

## Alternative flows

- **EF Core appears anywhere but `wa.infrastructure/Persistence`:** code review flags it (TA-2.3 — EF never leaks into core).
- **A new package is needed:** ADR line added to TA-17 first, then the reference (golden rule 1).

## Acceptance criteria

```gherkin
Given a type in wa.domain
When I inspect its references
Then it has no NuGet or project references at all (BCL only)

Given wa.application references only wa.domain
When I add a use case that needs the database
Then the port interface lives in wa.application/Ports and the EF adapter in wa.infrastructure/Persistence
And no IQueryable<T> appears in any use case signature

Given wa.api references wa.infrastructure (composition root)
When I inspect the solution
Then wa.api never references wa.workers directly
```

## Edge cases

- "Temporary" helper in the wrong layer: allowed to be created, but must be moved before merge — the rule is about steady state.
- `wa.workers` needs a port that only exists for jobs: job-local ports live in `wa.workers/Adapters` (TA-2.3).

## UI notes

- None.

## Technical notes

- This story is enforced by build + review, not by code — there is no analyzer package at M0 (adding one would need an ADR).
- CQRS naming: `{Verb}{Noun}Command` / `{Verb}{Noun}Query` (TA-2.4) — e.g. `FinalizeTransferCommand`.

## Links

- Feature: `F-FND-001-solution-scaffold.md` (FR-049-2, FR-049-4, AC-049-2)
- Architecture: TA-2.2, TA-2.3, TA-2.4
- Milestone: T-001
