# T-014-01 — i18next init + 8 locale resource bundles (TA-8.5)

**Story:** US-014-01-localized-ui
**Spec:** F-TRF-014-i18n.md
**Size:** S
**Depends on:** none (first task of F-TRF-014)

## Context to read (only these)

- docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md — FR-014-1, FR-014-2, Technical notes
- docs/features/Phase 0-MVP/F-TRF-014/US-014-01-localized-ui/US-014-01-localized-ui.md — Happy path + Technical notes
- docs/03-technical-architecture.md — "TA-8.5 i18n" (key scheme, locale files)

## Instructions

1. Add `i18next` + `react-i18next` to `wa.web`; create the resource bundles at `core/i18n/locales/{lang}.json` for all 8 languages per FR-014-1: en (default), nl, fr, es, pt, it, de, tr — English strings are source-of-truth.
2. Keys follow the scheme `{area}.{screen}.{control}` (TA-8.5); flat-ish nesting per key scheme.
3. Initialize i18next in `core/i18n` before first render with fallback language `en`; resolution mechanics land in T-014-03 — this task only ships the bundles + a stub resolver that returns `en`.

DECISION: D-16 (Open-Decisions-and-Constants.md) pins "en+nl+de+tr at launch, rest follow" — ship all 8 bundle files now (FR-014-1 lists all 8); do not gate file existence on the launch subset.
ASSUMPTION: `core/i18n/locales/{lang}.json` is the client-side copy; if a shared build step later emits these from English source, that's T-026 scope — confirm before adding tooling.

## Exit check

- [ ] FR-014-1 — en, nl, fr, es, pt, it, de, tr bundles present at `core/i18n/locales/{lang}.json` with `{area}.{screen}.{control}` keys (TA-8.5); all load without errors
- [ ] `t()` available in components via `react-i18next`; fallback language is `en`
- [ ] Stub resolver returns `en` (T-014-02 replaces it with the real detection order)

This task is the dependency for T-014-03/T-014-04 — its bundles are what AC-014-1 and AC-014-2 render through.

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-014-01 — i18next init + 8 locale resource bundles (milestone task T-026, F-TRF-014, TA-8.5).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md (FR-014-1, FR-014-2, Technical notes); US-014-01-localized-ui.md (Happy path + Technical notes); docs/03-technical-architecture.md TA-8.5 i18n.
Do exactly: add i18next + react-i18next; create core/i18n/locales/{lang}.json for en, nl, fr, es, pt, it, de, tr with keys {area}.{screen}.{control}; init in core/i18n before first render with fallback "en"; stub resolver returns "en" (T-014-02 replaces it).
Done when: FR-014-1's 8 locale files exist at core/i18n/locales/{lang}.json and load cleanly, t() works, fallback is "en".
Constraints: no new error codes or limits; D-16 governs launch subset (en+nl+de+tr) but all 8 files ship now per FR-014-1. Commit "feat: T-014-01 {one line}".
```
