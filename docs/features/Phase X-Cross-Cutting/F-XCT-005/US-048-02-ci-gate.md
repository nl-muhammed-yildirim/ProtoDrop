# US-048-02 — Catch a PII leak before release

**Feature:** F-XCT-005 — PII Policy (emails, sender names, telemetry) | **Status:** pending

---

**Story:** As a developer adding a new event or log line, I want the build to fail when a raw email, name, or IP reaches telemetry without the PII marker, so that the leak is caught in the PR — not in a regulator's question.
**Actor:** Developer on any feature branch.
**Goal:** Convention tests + CI grep: unmarked PII in an event payload or log template = red build.

## Preconditions

- The `[Pii]` attribute and `Pii=true` App Insights property exist (TA-9.4).
- The pipeline already has a lint step (the `translation_missing` grep precedent, EC-012-3).

## Happy path

1. Developer adds `Email` to a new event DTO without `[Pii]` → **convention test fails** with the property name and the fix ("mark `[Pii]` or hash it").
2. Developer logs `"sent to {Email}"` at Information without the PII slot → **log-template scan fails** (FR-048-5).
3. They mark the property `[Pii]` → build green; the App Insights sink emits it under the `Pii=true` custom property automatically.
4. The CI grep on event-helper call sites checks that no raw value is *passed as a plain string* where a helper expects the hashed form (the `translation_missing` pattern: a script in the pipeline, no new analyzer dependency).

## Alternative flows

- **False positive:** a `Name` that is a product name, not a person → mark `[NotPii]` (explicit escape hatch, documented in the feature spec's EC list review) — the marker system is "mark to be raw, or be clean," with a small, reviewable escape list.
- **Log-only fix:** replace the raw value with a masked display (`d****@x.com`) — allowed; masking is a *display* choice, the marker is the *storage* rule.

## Acceptance criteria

```gherkin
Given an event DTO with an unmarked Email property
When the unit suite runs
Then a convention test fails naming the property and the required fix

Given a log template containing a raw email at Information
When the build runs
Then the log-template scan fails and the fix is the PII marker or a masked value

Given a marked Pii property
When the event is emitted
Then App Insights stores it under the Pii=true custom property
```

## Edge cases

- The escape list (`[NotPii]`) must be reviewed like a flag: every use is visible in one file (FR-048-4 gate, EC review) — it's the only way a `Name` can be "not a person," and that's a claim that should be visible.
- The grep is a *script* (TA-17: no new NuGet dependency); if it outgrows a script, that's an ADR.
- Test fixtures that intentionally contain raw emails are `test-accounts@example.com` (TA-14.3) — the grep knows the fixture prefix.

## UI notes

- None (build system). The "failure" the user sees is the PR gate in the IDE/CI.

## Technical notes

- Convention tests in `wa.application.unit`: reflection over event DTOs — every `string` property matching `*Email*|*Name*|*Ip*` must carry `[Pii]` or `[NotPii]`; every `[Pii]` property must be in `ErasureScope.AllColumns` (the closed list, FR-048-2/6).
- Log-template scan: regex over `.Information(...)`/`.Warning(...)`/`.Error(...)` call sites for the same property-name pattern without the `{Message:Pii}` slot (TA-10.5).
- The App Insights sink enrichment reads the attribute — one mapping, tested by the enrichment unit test (FR-048-5).

## Links

- Feature: `XCT-005-pii-policy.md` (FR-048-3/4/5)
- Plan AC: AC-048-2, AC-048-3
- Related: US-012-01 (the correlation id that's safe by design), US-048-01 (what the markers protect)
- Architecture: TA-9.4, TA-10.5, TA-14 (gate location), TA-17 (no new dependency)
- Milestone: T-069
