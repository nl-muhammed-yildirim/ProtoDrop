# CHORE-code-style-2 — review r1

**Reviewer:** Opus 5.5 · **Date:** 2026-11-25 · **Range:** `origin/develop..d93dfd3` · **Verdict:** PASS (0 blocker, 0 major, 4 minor)

All 8 items and both DECISION fixes covered; behaviour otherwise preserved (47/47 routes same order, middleware/DI order unchanged, 76/76 log (level, template) pairs and all call-site args identical, clock reads 1:1 with Kind=Utc kept).

## Findings
- **[minor]** `src/wa.api/Endpoints/AdminGate.cs:20` — doc says "lower-cased admin emails"; set holds config values as written, compared case-insensitively.
- **[minor]** `src/wa.api/Endpoints/AdminGate.cs:3, 42-44` — stale comments copied from Program.cs.
- **[minor]** `scripts/check-telemetry-pii.mjs:383` — `Message =` regex reads only the first literal; a concatenated template would bypass the checks.
- **[minor]** `tests/wa.api.integration/PipelineTests.cs` plan row not applied and not listed as a deviation — harmless (format verify clean). No change needed.

## Gates
Build 0 C# warnings (25 npm-audit esproj) · `dotnet format --verify-no-changes` exit 0 · domain 243/243 · application 827/827 · workers 56/56 · PII guard 0 findings, selftest 31/31 + 16/16 · integration compiles; CORS tests 4/4 locally; full suite + Docker build run in PR CI. `PipelineTests` fails in isolation on develop too (pre-existing).

Fix round r1: Coder fixes minors 1–3.
