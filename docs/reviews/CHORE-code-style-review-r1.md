# CHORE-code-style — review r1

**Reviewer:** Opus 5.5 · **Date:** 2026-11-25 · **Range:** `origin/develop..b28af4c` · **Verdict:** FAIL (0 blocker, 2 major, 4 minor)

## Requirement coverage
| Requirement | Status | Evidence |
|---|---|---|
| One top-level type per file, named after the type | Met | Scan of src+tests: 0 violations (both `Program.cs` exempt per plan §1); 647/647 types preserved |
| XML docs on public+internal members, meaningful | Partial | Coverage met (0 CS15xx, now errors via `src/Directory.Build.props`); quality not met |
| Interfaces in `Ports/` or `<Area>/Interfaces/` | Met | 0 interfaces elsewhere |
| Zero behaviour change | Met | Code-line diff = enum reflow + guard allow-list paths + comment moves only |

## Findings
- **[major]** Generated docs mostly paraphrase member names (~451/529 new summaries, ~1,139/1,543 new `<param>`), e.g. `src/wa.domain/DownloadEvent.cs:16`, `src/wa.domain/Telemetry/TelemetryEvents.cs:55`, `src/wa.api/Middleware/CorrelationMiddleware.cs:19` ("Invokes the operation."), `src/wa.workers/WorkerServices.cs:29`. Violates plan §2.4.
- **[major]** Garbled / misleading summaries, e.g. `ITransferStore.cs:16` "Transfers the exists by link ID.", `:11`, `:21`; `ISearchTransfersStore.cs:10`; `ITransferSearchStore.cs:7`; `IAdminUserStore.cs:10`; `IOverviewStore.cs:9`; `TransferSizeExceededException.cs:15`.
- **[minor]** Trailing `//` comments became fragment summaries (`ScheduleWindow.cs:10,12`, `TelemetryEvents.cs:66,78`).
- **[minor]** `verify_plan.py --done` exits 1: it cannot parse MODIFY+DELETE / `git mv` rows. Tooling limitation, accepted as a gate exception.
- **[minor]** Stale path in living docs (`docs/REPO-FACTS.md:66`) and missing convention line in AGENT.md/AGENTS.md — fixed by orchestrator close-out. Historical plan/PROGRESS entries keep their original paths on purpose.
- **[minor / gap]** `tests/wa.api.integration` compiled but not run (Docker engine down on this machine).

## Gates
Build 0 errors, 66 unique warnings (= baseline), 0 CS15xx · domain 243/243 · application 824/824 · workers 56/56 · telemetry-PII check + selftest PASS · integration not run.

Fix round r1: Coder hand-rewrites the generated docs (docs-only, zero code changes).
