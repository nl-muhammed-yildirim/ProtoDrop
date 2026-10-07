# T-051-02 — Multi-stage Dockerfile + .dockerignore for wa-api

**Story:** US-051-03 | **Spec:** FR-051-2, AC-051-3 (build half), EC-051-4 context note | **Size:** M (two files)
**Depends on:** T-049-04 (wa.api exists and builds)

---

## Context to read (only these)

- `../../F-FND-003-ci-pipeline.md` → FR-051-2 + Technical notes (Dockerfile lines only)
- `US-051-03-image-build.md` → happy path + second Gherkin block

## Instructions

1. Create `src/wa.api/Dockerfile` — multi-stage: **publish** stage (`dotnet publish`) → final stage on **`aspnet:10.0`**.
2. Create `.dockerignore` at repo root excluding docs/tests/node_modules so the build context stays ~3 MB.
3. Build locally: `docker build -t wa-api:local-check src/wa.api`.

## Exit check

- [ ] `docker build -t wa-api:local-check src/wa.api` succeeds (multi-stage, aspnet:10.0 base)
- [ ] The image starts and serves `/health` when given the right env vars (second Gherkin block)
- [ ] Build context ~3 MB (.dockerignore working — check with `du -sh` on what gets sent, or just confirm docs/ is excluded)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, src/wa.api builds; Docker Desktop available).
Task T-051-02 — add the wa-api image build.
Read first (only): docs/features/Phase 0-Foundation/F-FND-003/F-FND-003-ci-pipeline.md (FR-051-2 + Technical notes Dockerfile lines) and US-051-03-image-build.md (happy path + second Gherkin block).
Do exactly:
1. Create src/wa.api/Dockerfile as a multi-stage build: publish stage (dotnet publish) then final stage on aspnet:10.0.
2. Create .dockerignore at repo root excluding docs, tests, node_modules — context must stay ~3 MB.
3. Run docker build -t wa-api:local-check src/wa.api and confirm it succeeds; then start the image with the right env vars (Local profile values from AGENT.md §5.2) and confirm /health responds 200.
Done when: the multi-stage build succeeds locally, the image serves /health, and the context stays small via .dockerignore.
Constraints: base image aspnet:10.0 pinned by major (minor updates OK; breaking = ADR); no push to any registry in this task (push is gated on AZURE_CONTAINER_REGISTRY — E-003).
```
