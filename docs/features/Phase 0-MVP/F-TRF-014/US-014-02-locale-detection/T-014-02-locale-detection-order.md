# T-014-02 — Locale detection order in core/i18n (account → browser → en)

**Story:** US-014-02-locale-detection
**Spec:** F-TRF-014-i18n.md
**Size:** S
**Depends on:** T-014-01 (i18next init + bundles; replaces its stub resolver)

## Context to read (only these)

- docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md — FR-014-3, AC-014-3, Technical notes
- docs/features/Phase 0-MVP/F-TRF-014/US-014-02-locale-detection/US-014-02-locale-detection.md — Happy path + Alternative flows + Edge cases
- docs/03-technical-architecture.md — "TA-8.5 i18n" (resolution before first render)

## Instructions

1. Replace T-014-01's stub resolver with the FR-014-3 detection order in `core/i18n`: **account setting (`localStorage` account-locale override / cached `/auth/me`) → `navigator.language` → default `en`.** For signed-in users the account setting wins over the browser (AC-014-3).
2. Resolve **synchronously and cheaply, before first render** — read `localStorage` / cached `/auth/me`; no network await on the critical path beyond what sign-in already did. This is distinct from the theme pre-paint path (F-TRF-015): both run before paint but own separate decisions (no cross-coupling).
3. Unsupported browser language (e.g. `ja`) falls through to `en` — silently, no "language not available" ceremony. Guests are re-detected on the next load (no live switching in MVP); signed-in users stay on their account setting.
4. Put the resolved locale into the `app.initialized` telemetry properties (helps F-TRF-012's language-mix dashboard) — not a new event.

DECISION: detection is per-browser for guests, per-account for signed-in users; the same user on two browsers with differing browser settings sees two languages in guest mode — expected and documented (US-014-02 Edge cases).
ASSUMPTION: `localStorage` account-locale override key name is not pinned in Open-Decisions-and-Constants.md Part 2 — confirm against F-TRF-008's account-locale storage before locking it.

## Exit check

- [ ] AC-014-3 — signed-in user with account locale `nl` and a German browser → Dutch used (account setting wins over the browser)
- [ ] FR-014-3 — detection order is account setting → browser → en, resolved before first render (no flash of another language for guests)
- [ ] Guest with browser language `de`, fresh load → German resolved before first render (AC-014-1 detection half; rendering half is T-014-03)
- [ ] Guest with unsupported browser language (e.g. `ja`) → `en` default used, silently
- [ ] Signed-in user after `localStorage` cleared → account setting re-hydrates from `/auth/me`; no language flip

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-014-02 — locale detection order in core/i18n (milestone task T-026, F-TRF-014, TA-8.5).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md (FR-014-3, AC-014-3, Technical notes); US-014-02-locale-detection.md (Happy path + Alternative flows + Edge cases); docs/03-technical-architecture.md TA-8.5 i18n.
Do exactly: replace T-014-01's stub resolver with FR-014-3 order (account setting → navigator.language → en) in core/i18n, resolved synchronously before first render; unsupported browser language falls through to en silently; put resolved locale into app.initialized telemetry properties.
Done when: AC-014-3 holds (account nl beats German browser), guest de resolves before first render, and ja falls to en — the FR-014-3 order is verifiable in tests.
Constraints: depends on T-014-01; no new error codes or limits; distinct from F-TRF-015 theme pre-paint path (no cross-coupling). Commit "feat: T-014-02 {one line}".
```
