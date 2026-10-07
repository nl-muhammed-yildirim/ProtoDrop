# US-053-01 — Ask "what are this plan's limits?" in one call

**Feature:** F-FND-005 — Limits Registry | **Status:** done (T-005, 2026-09-01)

---

**Story:** As a feature developer (upload pre-checks, server-side enforcement, billing), I want to ask for a plan's effective limits through one call, so that no feature hardcodes a limit value.
**Actor:** Developer (human or AI session); every plan-gated feature from T-009 on (F-TRF-007, F-BIL-*).
**Goal:** `ILimitsProvider.Resolve(planCode)` returns the full effective limits for any seeded plan — and guests resolve to Free.

## Preconditions

- Plans seeded (F-FND-004 / US-052-02): free/pro/business rows with Part-2 `LimitsJson`.
- Limits Registry in place (T-005).

## Happy path

1. Feature code calls `Resolve("pro")` (or "free" / "business").
2. The provider reads the plan row through `PlansCache` and returns a `LimitsRecord` with every constant from Feature Plan Appendix A populated.
3. Guest requests (no account) resolve to the Free plan's limits — no account required to know a limit exists (FR-053-6).

## Alternative flows

- **Unknown plan:** `Resolve("enterprise")` fails with a typed error mapping to 404 `PLAN_UNKNOWN` — loud, not silent (AC-053-4).
- **DB down on cache miss:** resolution fails with a typed error; callers map per TA-4.1.3 — no silent fallback to hardcoded defaults (EC-053-3).

## Acceptance criteria

```gherkin
Given the seeded plans (F-FND-004)
When I call Resolve("free"), Resolve("pro"), and Resolve("business")
Then each returns exactly its Part-2 constant values

When a guest request needs limits before any account exists
Then it resolves to the Free plan's limits without an account lookup
```

## Edge cases

- Unknown plans fail loudly (404 `PLAN_UNKNOWN`) — never silently default.
- Feature-gate flags (`feature.*`) are ignored here; gates are read separately (FR-053-3).

## UI notes

- None at M0 (no admin screen yet — that's F-XCT-001 / T-065).

## Technical notes

- `LimitsRecord` is a BCL-only POCO in `wa.domain`; resolution logic lives in `wa.application/Limits/` behind the `IPlansStore`/`IFlagsStore` ports (TA-0.2 rule 7 — no DB reads in the domain).
- This is the M0 half of F-TRF-007: resolution here, enforcement at finalize/send with T-020.

## Links

- Feature: `F-FND-005-limits-registry.md` (FR-053-1, FR-053-6, AC-053-1)
- Architecture: TA-3.4
- Milestone: T-005
