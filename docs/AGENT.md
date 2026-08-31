# AGENT.md — AI Developer Operating Manual

**Last updated:** 2026-08-31
**Read this file first, every session.** It contains everything a fresh AI session needs: what to read, how to run, what may be decided alone, and how to escalate.

---

## 1. Session bootstrap (in this order)

1. **This file** — operating rules.
2. `Open-Decisions-and-Constants.md` — the values you may/may not invent (Part 2 = frozen constants).
3. `Milestone-Backlog.md` — find `CURRENT` task; do only that one.
4. The spec files for that task: `02-feature-plan.md` (FR/AC IDs), `03-technical-architecture.md` (TA IDs), `features/<Phase>/<feature-id>/` (per-feature detail + user story files).
5. `UI-Reference.md` for any UI work; `QA-SCRIPTS.md` when the task's exit check is a manual browser pass; `Preflight-Checklist.md` if a human-side dependency looks blocking.

## 2. Stack (pinned)

| Item | Value |
|---|---|
| .NET | **10** (SDK via `global.json`, rollForward latestMajor) |
| Pattern | **Clean (onion)**: `wa.domain → wa.application → wa.infrastructure → wa.api / wa.workers` |
| CQRS | **MediatR 12.5** (frozen version), FluentValidation behaviors |
| EF Core | 10.x — confined to `wa.infrastructure/Persistence` (TA-2.3) |
| Azure | SQL, Blob, Service Bus, Functions (Flex), Front Door, Communication Hub, App Insights, Key Vault |
| Frontend | React 18 + TS (strict) + Vite 5 + Tailwind 3 + TanStack Query 5 + Zustand 5 + i18next + `@azure/storage-blob` |
| IDE | **Visual Studio 2026** (primary). .NET SDK 10, Node 20 LTS, Docker Desktop (WSL2), Git |

See `Getting-Started-VS2026.md` for the full machine setup.

## 3. Golden rules (mirrors TA-0.2)

1. No new NuGet/npm package without an ADR line (TA-17).
2. No literal limit values — always from the Limits Registry (TA-3.4).
3. Background events use the TA-5.2 envelope + TA-5.3 event types only.
4. Every schema change is an EF Core migration in `wa.infrastructure/Persistence`.
5. Errors always use the Problem+JSON shape (TA-4.1.3); code list is closed.
6. Telemetry only from TA-10.2 names.
7. Dependency rule: domain → application → infrastructure → (api, workers). EF/Azure never in domain.
8. One MediatR `IRequest`/handler per use case; no `DbContext` in domain/use cases.
9. Domain events via `IEventPublisher` — MediatR-free.

## 4. Test gate (Definition of Done, per task)

Run **all** of these before marking a task done:

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.api.integration     # Testcontainers: Docker must be running
cd src/wa.web
npm run lint && npm run test && npm run build
```

Plus, per task: the task's own **exit check** in `Milestone-Backlog.md`.

## 5. Local run

### 5.1 `docker-compose.local.yml` (repo root)

```yaml
services:
  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "YourStrong!Passw0rd"
      MSSQL_PID: "Developer"
    ports: ["1433:1433"]
  azurite:
    image: mcr.microsoft.com/azure-storage:latest
    command: ["azurite-blob", "--blobHost", "0.0.0.0"]
    ports: ["10000:10000"]
```

```powershell
docker compose -f docker-compose.local.yml up -d
```

### 5.2 Local env vars (in `wa.api/Properties/launchSettings.json`, profile `Local`)

| Variable | Local value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Local` |
| `ConnectionStrings__WaDb` | `Server=localhost,1433;User Id=sa;Password=YourStrong!Passw0rd;Database=wa;TrustServerCertificate=True` |
| `Wa:Blob:AccountUrl` | `http://127.0.0.1:10000/devstoreaccount1` |
| `Wa:Blob:AccountKey` | `Eby8vdM02xNOcqTqK…> (Azurite dev key) |
| `Wa:ServiceBus:ConnectionString` | *(optional locally; use in-memory publisher fake if unset)* |
| `Wa:CommunicationHub:Connection` | *(optional locally; log-only sender if unset)* |
| `Wa:Jwt:Secret` | 64 hex chars (any fixed dev value) |
| `Wa:Url:Public` | `http://localhost:5173` |
| `Wa:Url:Api` | `http://localhost:8080` |
| `Wa:Cors:AllowedOrigins` | `http://localhost:5173` |
| `Wa:AdminEmails` | `dev@example.com` |
| `Wa:Zip:SignKey` | any 32+ chars |

### 5.3 Run

```powershell
# API (VS 2026: set wa.api as start project, F5) → http://localhost:8080/health
# Web (terminal from VS)
cd src/wa.web
npm ci && npm run dev    # http://localhost:5173
```

Migrations:
```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/wa.infrastructure --startup-project src/wa.api
dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api
```

## 6. Where specs live (cite IDs, don't paraphrase)

| Spec | File |
|---|---|
| Product intent | `01-product-analysis.md` |
| Features (FR/AC/EC), limits, entities, events | `02-feature-plan.md` |
| Per-feature detail + **user story files** | `features/<Phase>/<feature-id>/` (index: `features/README.md`) |
| Architecture (TA-*) | `03-technical-architecture.md` |
| Production/SLOs | `04-production-plan.md` |
| Manual QA scripts ("manual pass" exit checks) | `QA-SCRIPTS.md` |
| Design | `UI-Reference.md` |

## 7. Autonomy boundary

**You may decide alone (no escalation):**
- File/folder layout details inside a project
- Internal helper names, private methods, local variables
- Test implementation details (frameworks already pinned)
- CSS specifics that don't violate `UI-Reference.md` tokens
- Which index hint to add, as long as TA-3.2/3.3 stay consistent
- Log message wording (PII rules from TA-9.4 apply)

**You MUST escalate (decision request, §8) before:**
- Any new NuGet/npm package
- Any schema change beyond what the current task's spec says (new table/column = new migration + ADR note)
- Any change to limit values (even "temporary")
- Anything PII- or security-shaped (SAS TTLs, cookie flags, token lifetimes)
- Any new Azure resource or environment variable
- Any cost line > ~$50/month
- Changing a `TBD-you` decision that isn't yet `confirmed` in `Open-Decisions-and-Constants.md`

## 8. Escalation format

Post a decision request with exactly:

```
DECISION: {one-line question}
CONTEXT: {why it matters, 1–3 lines}
OPTIONS:
  A) {option} — {tradeoff}
  B) {option} — {tradeoff}
RECOMMEND: {A or B, one line of justification}
BLOCKS: {task ID, or "none"}
```

Log every decided item in `Open-Decisions-and-Constants.md` Part 3 (append-only).

## 9. Dev loop

1. Pick `CURRENT` from `Milestone-Backlog.md`.
2. Read its spec links (feature file + user story files + TA sections).
3. Implement smallest slice → tests encoding each AC → run the test gate (§4).
4. Mark task `done` with the date; move `CURRENT` down.
5. One task at a time. Never skip. Blocked-only-by-decisions: if blocked, open a decision request and stop.

## 10. Current task pointer

`CURRENT` = first non-`done` task in `Milestone-Backlog.md`.
