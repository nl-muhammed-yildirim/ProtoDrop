# Getting Started — Visual Studio 2026 + .NET 10

**Last updated:** 2026-08-23
How to open, run, and test this project on your machine. **Visual Studio 2026 is the designated development environment** for this project; .NET SDK 10 is pinned via `global.json`.

---

## 1. One-time setup (machine)

| Tool | Version | Why |
|---|---|---|
| Visual Studio 2026 | latest | Workloads: *ASP.NET and Web Development* + *.NET Desktop Development* |
| .NET SDK | 10.x | `global.json` pins 10.0.100 (`rollForward: latestMajor`) |
| Node.js | 20 LTS | `src/wa.web` (React + Vite) |
| Docker Desktop | WSL2 backend | local SQL + Azurite |
| Git | any | source control |

Verify:
```powershell
dotnet --list-sdks    # must show a 10.x entry
node -v               # v20.x
docker version
```

## 2. Get the repo

```powershell
cd C:\Users\myild\workspace\ProtoDrop   # project folder
git init
git add .
git commit -m "docs: planning + operating docs (T-000)"
```

## 3. Create the solution scaffold (this *is* task T-001)

In VS Developer PowerShell, in `C:\Users\myild\workspace\ProtoDrop`:

```powershell
dotnet new sln -n wa
echo '{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }' > global.json

# Clean (onion) layout per TA-2.1
dotnet new classlib -n wa.domain         -o src/wa.domain
dotnet new classlib -n wa.application    -o src/wa.application
dotnet new classlib -n wa.infrastructure -o src/wa.infrastructure
dotnet new web      -n wa.api            -o src/wa.api
dotnet new worker   -n wa.workers        -o src/wa.workers
dotnet new xunit    -n wa.domain.unit         -o tests/wa.domain.unit
dotnet new xunit    -n wa.application.unit    -o tests/wa.application.unit
dotnet new xunit    -n wa.api.integration     -o tests/wa.api.integration
dotnet sln add src/wa.domain src/wa.application src/wa.infrastructure src/wa.api src/wa.workers tests/wa.domain.unit tests/wa.application.unit tests/wa.api.integration

# Project references (TA-2.2)
dotnet add src/wa.application reference src/wa.domain
dotnet add src/wa.infrastructure reference src/wa.application
dotnet add src/wa.api reference src/wa.infrastructure
dotnet add src/wa.workers reference src/wa.infrastructure
dotnet add tests/wa.domain.unit reference src/wa.domain
dotnet add tests/wa.application.unit reference src/wa.application
dotnet add tests/wa.api.integration reference src/wa.api

# TA-2.6 packages (frozen set)
dotnet add src/wa.application package MediatR --version 12.5
dotnet add src/wa.application package FluentValidation
dotnet add src/wa.application package FluentValidation.DependencyInjectionExtensions
dotnet add src/wa.infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/wa.infrastructure package Azure.Storage.Blobs
dotnet add src/wa.infrastructure package Azure.Identity
dotnet add src/wa.infrastructure package Azure.Messaging.ServiceBus
dotnet add src/wa.infrastructure package Azure.Communication.Email
dotnet add src/wa.infrastructure package Stubble
dotnet add src/wa.api package MediatR --version 12.5
dotnet add src/wa.api package Serilog.AspNetCore
dotnet add src/wa.api package Serilog.Sinks.ApplicationInsights
dotnet add src/wa.api package Swashbuckle.AspNetCore
dotnet add src/wa.api package AspNetCoreRateLimit
dotnet add tests/wa.api.integration package Testcontainers
dotnet add tests/wa.api.integration package Testcontainers.MsSql

# Frontend (in-solution via package.json, TA-2.1)
cd src
npm create vite@latest wa.web -- --template react-ts
cd wa.web
npm i @tanstack/react-query zustand i18next react-i18next @azure/storage-blob
npm i -D tailwindcss
```

Health endpoint in `src/wa.api/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", db = "ok", sb = "ok" }));
app.Run();
```

Plus `docker-compose.local.yml` (exact content in **AGENT.md §5.1**) and `src/wa.api/Properties/launchSettings.json` with the `Local` profile: port 8080 + all env vars from **AGENT.md §5.2**.

## 4. Open in Visual Studio 2026

1. **File → Open → Project/Solution** → `wa.sln`.
2. Let NuGet restore finish (first time is slow).
3. Set `wa.api` as **Start Project**.
4. **View → Terminal** and start the data tier:
   ```powershell
   docker compose -f docker-compose.local.yml up -d
   ```
5. **F5** → open `http://localhost:8080/health` → expect `{"status":"ok",...}`.
6. Second terminal: `cd src/wa.web && npm run dev` → `http://localhost:5173`.

## 5. Run the test gate (Definition of Done for every task)

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.api.integration      # Testcontainers: Docker must be running
cd src/wa.web
npm run lint && npm run test && npm run build
```
(In VS: Test Explorer for the .NET sides; web commands from the terminal.)

## 6. Migrations

EF Core lives only in `wa.infrastructure/Persistence` (TA-2.3 / ADR-015):

```powershell
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/wa.infrastructure --startup-project src/wa.api
dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api
```

## 7. Troubleshooting

| Symptom | Fix |
|---|---|
| `global.json` SDK not found | install .NET 10 SDK; `dotnet --list-sdks` |
| `Failed to connect to SQL` | Docker not running / port 1433 busy |
| Azurite not listening on 10000 | `docker compose -f docker-compose.local.yml ps`; check logs |
| CORS error in dev | `Wa:Cors:AllowedOrigins` must include `http://localhost:5173` |
| `sig=` in logs | log masking not wired yet (TA-9.6) — expected pre-T-008 |
| npm install fails on Windows | Node 20 + Docker Desktop (WSL2); check proxy |

## 8. Where you are in the plan

- Steps 3–5 = tasks **T-001/T-002** in `Milestone-Backlog.md`.
- First real feature work: **T-004** (domain + DDL → EF migrations), then M1.
