# US-052-03 — Keep the domain layer free of infrastructure

**Feature:** F-FND-004 — Domain Model & Database Schema | **Status:** done (T-004, 2026-09-01)

---

**Story:** As a developer adding entities or domain rules, I want `wa.domain` to compile with zero external references, so that the domain never silently depends on EF, Azure, MediatR, or any other framework.
**Actor:** Developer (human or AI session), code review (enforces the rule).
**Goal:** `wa.domain` is BCL-only — every POCO in it could be copied into any project and still compile.

## Preconditions

- Solution scaffolded per F-FND-001 (T-001) with the TA-2.2 dependency rule.
- Entities added for T-004.

## Happy path

1. Developer adds a new POCO to `wa.domain` (e.g. `LimitsRecord` arrives later in T-005).
2. The project compiles with no NuGet or project references — BCL only.
3. EF-specific concerns (`WaDbContext`, `IEntityTypeConfiguration<T>`) live in `wa.infrastructure/Persistence`, never in the domain.

## Alternative flows

- **A developer needs an EF attribute on a POCO** → move it to an infrastructure configuration class; if that's impossible, open a decision request (AGENT.md §8) — the rule is load-bearing for testability (TA-0.2).
- **CI:** the build itself enforces the rule — any added reference shows up in review and breaks the "BCL only" expectation of AC-052-3.

## Acceptance criteria

```gherkin
Given wa.domain compiles as part of the solution
When I inspect its project file references
Then it has zero external NuGet or project references (BCL only)

Given an entity added to wa.domain in T-004
When I look for EF attributes, DbContext, or configuration classes inside wa.domain
Then none exist — all persistence concerns are in wa.infrastructure/Persistence
```

## Edge cases

- `LimitsRecord` (T-005 / F-FND-005) is the first POCO that later gains *behavior* — it still stays BCL-only; its resolution logic lives in `wa.application`.
- A "temporary" reference is a golden-rule violation: no package without an ADR line (TA-17, TA-0.2 rule 1).

## UI notes

- None.

## Technical notes

- This is the testable half of FR-052-1/FR-052-3: AC-052-3 encodes it as an inspectable fact (project file references), not a convention.
- The 15 T-004 POCOs are the first proof; every later feature's entities must keep passing this story's ACs.

## Links

- Feature: `F-FND-004-domain-model-schema.md` (FR-052-1, FR-052-3, AC-052-3)
- Architecture: TA-2.2, TA-0.2
- Milestone: T-004
