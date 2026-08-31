# US-044-02 — Gate a phase feature with a flag

**Feature:** F-XCT-001 — Feature Flags & Remote Limits | **Status:** pending

---

**Story:** As a product owner, I want each Phase 1/2/cross-cutting feature to sit behind a single flag (`feature.search`, `feature.dataExport`, `feature.emailSettings`, `feature.ads`, `feature.scheduling`, `feature.analytics`), so that a half-finished feature ships in code but off, and I can switch it on per environment when it's ready.
**Actor:** Product owner / operator.
**Goal:** One boolean per feature; off = invisible UI + documented API behavior; on = live; no deploy either way.

## Preconditions

- Seeded keys `feature.search`, `feature.dataExport`, `feature.emailSettings` exist (T-065); Phase 1 keys `feature.ads`/`feature.scheduling`/`feature.analytics` already exist per TA-13.2.
- Each gated feature checks its gate through `ResolveFlag` — never a code constant.

## Happy path

1. In dev: `feature.dataExport` is `true` → the Profile → Export screen renders and the export API answers.
2. In staging: the flag is `false` → the same code hides the Export entry and the API returns 404 `FEATURE_DISABLED`.
3. Launch day: prod flag flipped to `true` in the Flags screen → feature live within 30 s, no release.
4. Incident day: flip back to `false` → same kill path.

## Alternative flows

- **Half-on state:** a flag is global (all users of the environment). Per-plan availability is a *plan* concern (`ANALYTICS`, `SCHEDULING` rows in Appendix A), not a flag concern — the flag says "the feature exists in this environment at all".
- **Gate checked client-side only:** not enough — the API must return 404 `FEATURE_DISABLED` when its flag is off, so a cached SPA can't leak a dead endpoint.

## Acceptance criteria

```gherkin
Given feature.dataExport is false in staging
When a signed-in user navigates to their export screen
Then the export entry is not rendered
And GET on the export API returns 404 with code FEATURE_DISABLED

Given feature.dataExport is true in dev
When a signed-in user requests an export
Then the export proceeds (F-XCT-004)
```

## Edge cases

- Absent key = feature off (FR-044-3 default) — a deleted flag fails *closed*, which is the safe direction.
- The closed TA-10.2 event list is not extended by gates; gating logic is silent except `admin_action` on the flag write itself.
- Feature-specific kill behavior (e.g., search returns empty vs. 404) is defined by that feature's file, but the default contract is 404 `FEATURE_DISABLED`.

## UI notes

- Flags screen **Features** group: switch per `feature.*` key, tooltip = one-line "what turns on".
- Gated UI entries are *absent* (not disabled) when off, so users never see a greyed-out feature.

## Technical notes

- `ResolveFlag(key, default)` (FR-044-3); gates evaluated once per request via the 30 s cache.
- 404 code `FEATURE_DISABLED` added to the closed Problem+JSON code list (TA-4.1.3) by T-065.
- The gate call is the **only** allowed difference between environments for these features (no `#if`, no environment-name checks).

## Links

- Feature: `XCT-001-feature-flags.md` (FR-044-3/5/7)
- Plan AC: AC-044-2
- Related: US-045-01/02 (search gate), US-046-01/02 (email settings gate), US-047-01/02 (data export gate), US-024-04 (ads gate — Phase 1 pattern this generalizes)
- Architecture: TA-3.4, TA-13.2, TA-4.1.3
- Milestone: T-065
