# CHORE-code-style — one type per file, XML docs, interface folders

**Type:** solution-wide, non-feature refactor (no T-xxx id). **Branch:** `chore/code-style-cleanup` (from origin/develop @ 14f5947). **Plan date:** 2026-11-25.
**User intent (summarised):** (1) one class/record/interface per file; (2) C# XML documentation tags (summary / param / returns) on the code; (3) interfaces in their own folder, either project-level or inside the folder they belong to.

## 1. Goal

Every C# file under src/ (and tests/ for rule 1) declares exactly one top-level type named like the file, every interface of `wa.application` lives in `Ports/` or an `<Area>/Interfaces/` folder, and every public/internal type and member in src/ has meaningful XML docs. **Exit check:** `dotnet build src/wa.slnx` is green with the doc diagnostics promoted to errors (Phase C), with no new non-doc warnings over the 66-warning baseline, and the four test projects pass.

### Scope defaults (set by the orchestrator, adopted here)
- **In:** C# backend src/wa.api, src/wa.application, src/wa.domain, src/wa.infrastructure, src/wa.workers. Rule 1 also covers `tests/*`.
- **Out:** src/wa.web (TypeScript), `src/wa.infrastructure/Persistence/Migrations/**` (EF-generated; they already carry `/// <inheritdoc />` and raise 0 CS1591 today), docs/ path references (see §5.4).
- **Pure refactor.** Zero behaviour change, no new NuGet/npm packages, no route/wire/JSON-name change, no schema change, no namespace change (§2.3). The MediatR request/handler set, the DI registrations and the ProblemCodes/TelemetryEvents closed sets keep the same meaning.

### Measured baseline (2026-11-25, this branch)
- 425 `.cs` files scanned (src + tests, Migrations/bin/obj excluded). **97 files hold more than one top-level type**: wa.application 77, wa.domain 7, wa.infrastructure 4, wa.api 1, wa.workers 0; tests: wa.api.integration 6, wa.application.unit 1, wa.workers.unit 1. (The orchestrator's rough survey said 14 test files; it counted nested types. Nested types are not split, see §2.1.)
- **10 single-type files are misnamed** (file name differs from the type name), all in wa.application.
- **14 single-type interface files** in `UseCases/*` sit next to handlers. All 57 interfaces of the solution are in `wa.application`. wa.domain, wa.infrastructure, wa.api and wa.workers declare none.
- `dotnet build src/wa.slnx -p:GenerateDocumentationFile=true` gives **709 unique CS1591 sites in src** (api 17, application 316, domain 195, infrastructure 161, workers 20). It also gives 11 malformed-doc sites (CS1570, CS1573, CS1574), and 0 in Migrations or generated code. A heuristic scan of already-documented members finds **about 243 members with a summary but no `<param>`/`<returns>`**.
- Two `internal` top-level types (`SasLogMask`, `TransferTrackerExtensions`) and about 12 internal members. CS1591 never reports internal members.
- Plain `dotnet build src/wa.slnx -t:Rebuild` baseline: **66 unique warnings** (CS0618 x49, CS8524 x4, CS8604 x5, CS8609, CS8619 x2, CS8620, CS8631, CS8764 x3). These are out of scope; the count must not grow.
- No `Directory.Build.props`, no `.editorconfig`, no `TreatWarningsAsErrors`, no `InternalsVisibleTo`, no `file`-scoped types, no source generators (GeneratedRegex / LoggerMessage / JsonSerializable) in src or tests. The only `partial` is `public partial class Program { }` at `src/wa.api/Program.cs:1532` (WebApplicationFactory entry point). Keep it in Program.cs: the top-level statements plus that partial are one type, `Program`.

## 2. Design & Decisions

### 2.1 Rule 1 — one top-level type per file (precise definition)
- A **type** is any `class`, `record`, `record struct`, `struct`, `interface`, `enum` or `delegate`. A file under src/ or tests/ (Migrations excluded) declares **exactly one top-level (namespace-scope) type**.
- **File name = type name + `.cs`.** A generic type uses its bare name (`CapturingLogger<T>` → `CapturingLogger.cs`). `Name{T}.cs` is used only if a generic and a non-generic type with the same name share a folder (none today). Test classes keep their existing names (`ScheduledSendEndpointTests_GateOff.cs` as-is).
- **Nested types count as part of their parent and are NOT extracted.** This covers all 306 nested types today: private fakes in tests, private cache `Entry` records, `StubbleTemplateRenderer`'s private render models, and 4 public nested src types. The public ones are `CheckoutException.FailureCode` (referenced as `CheckoutException.FailureCode.*` at `src/wa.api/Program.cs:1479` and `tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs:74`), `InMemoryEventPublisher.StoredEnvelope`, and `TransferAnalyticsStore.DayCount` / `FileKeyCount`. Un-nesting them would rename every reference, which is churn with no gain. A split must **not** dodge the rule by nesting a top-level type into another one.
- Each type moves with its leading `///` block, its attributes and any `//` comment directly above it. A file-header `//` comment above the `namespace` line goes to the file of the primary type: the type the old file was named after, or the handler when no type had that name. Each new file gets the same `namespace` line and only the `using`s it needs (ImplicitUsings is on in every project). Bodies are moved byte-for-byte; the order of members inside a type does not change.
- **Placement:** a split-out type goes into the **same folder** as its source file, except interfaces (§2.2). The csproj files are SDK-style with globbing (no `<Compile>` items anywhere), so no csproj edits are needed. Use `git mv` for whole-file moves and renames so history follows.

### 2.2 Rule 3 — where interfaces live (one convention)
- **src/wa.application/Ports/ already is the interfaces folder** of the application layer. TA-2.1 defines it as "abstractions (outward-facing)" and TA-2.3 says ports are interfaces. Its interfaces stay there, one per file. Its non-interface port contract types (EmailMessage, IdempotencyEntry, FileEntry, TemplateData, ComponentStatus, DomainEventRecord, PlanUnknownException) appear in the port signatures, so they stay next to them in `Ports/`.
- **Every other interface goes into an `Interfaces/` subfolder of the folder that owns it**: `Limits/Interfaces/`, and `UseCases/<Area>/Interfaces/` for Admin, Auth, Billing, DataExport, EmailSettings, Gdpr and Transfers. This is the "folder it belongs to" option of the request. It keeps each use-case area self-contained (TA-2.4 `UseCases/{Feature}/`) and does not mix area-local store contracts into the cross-cutting `Ports/`.
- The DTO records, enums and exceptions that today share a file with an area interface (e.g. `AdminUserRow` in `IAdminUserStore.cs`) go to the **area root** (`UseCases/Admin/AdminUserRow.cs`), not into `Interfaces/`. The `Interfaces/` folder holds interfaces only.
- Rule for later work: a new port interface goes into `Ports/` when more than one use-case area or an outside adapter consumes it, and into `<Area>/Interfaces/` otherwise.

### 2.3 Namespaces do NOT follow the folder move (decided; AGENT.md §7 "file/folder layout details inside a project")
- Every moved or split type keeps its current namespace (`wa.application.UseCases.Transfers`, `wa.application.Limits`, …). **No `.Interfaces` suffix.**
- *Why:* moving the namespace would add a `using ...Interfaces;` line to every same-area handler (they need none today), to the infrastructure adapters (40 files in Persistence alone), to `src/wa.api/Program.cs`, to `src/wa.workers/WorkerServices.cs`, and to every hand-rolled stub in the 4 test projects. That is hundreds of edits with merge-conflict risk against in-flight Hermes branches and no behavioural value.
- *Consequences:* no `using` changes at all. MediatR's assembly scan (`src/wa.api/Program.cs:35`, RegisterServicesFromAssembly(typeof(PingCommand).Assembly)) and EF ApplyConfigurationsFromAssembly (`src/wa.infrastructure/Persistence/WaDbContext.cs:29`) are assembly-based, so they are unaffected. The only cost is IDE0130 ("namespace does not match folder structure") under `Interfaces/`. Phase C silences it in `src/.editorconfig`. It is a style suggestion and never reaches the build output (no EnforceCodeStyleInBuild).

### 2.4 Rule 2 — XML documentation standard (src only; tests exempt)
- **Required on** every `public` and `internal` type and member in src/ (Migrations excluded): types, constructors, methods, properties, fields/constants, enum members, events, delegates, and the positional parameters of records (as `<param>` on the record, which Roslyn also treats as the doc of the generated property).
- **Tags:**
  - `<summary>` always.
  - `<param name="…">` for **every** parameter, including CancellationToken (one standard sentence is fine).
  - `<returns>` on every member that returns a value (not void, not plain Task). For `Task<T>`, describe the T.
  - `<typeparam>` on generic types and members.
  - `<exception cref="…">` for each exception a member throws on purpose (the domain/validation exceptions that map to ProblemCodes).
  - `<inheritdoc />` **only** on interface implementations and overrides whose contract doc lives on the interface or base. A MediatR `Handle` method uses `<inheritdoc />`, and the use-case doc goes on the handler class.
- **Content:** describe intent and contract: what it does, why, units, null semantics, side effects (e.g. "publishes TransferSent"). Do not paraphrase the name. "Gets or sets the X" is acceptable only when nothing more can be said. Keep existing FR-/AC-/EC-/TA-/US-/T- ids. When a `//` comment above a member explains intent, **move** its text into `<summary>`/`<remarks>` (no duplicate, no loss). `//` comments inside method bodies stay where they are.
- **Fix the 11 existing malformed sites:**
  - CS1570 (unescaped `<` or `&` in prose; escape as `&lt;` / `&amp;`): `src/wa.api/Middleware/SessionMiddleware.cs:8`, `src/wa.application/UseCases/Transfers/GetMyTransfer.cs:20`.
  - CS1573 (missing param tags): `src/wa.application/Ports/BlobStore.cs:14`, `src/wa.application/Ports/BlobStore.cs:37`, `src/wa.application/UseCases/Admin/IAdminUserStore.cs:19`, `src/wa.application/UseCases/Admin/IOverviewStore.cs:11`, `src/wa.application/UseCases/Admin/ISearchTransfersStore.cs:31`, `src/wa.infrastructure/Email/CommunicationHubEmailSender.cs:17`.
  - CS1574 (unresolvable cref): `src/wa.infrastructure/Blob/BlobSasMinter.cs:33`. `cref="SetPermissions"` must become a resolvable cref or `<c>SetPermissions</c>`.
  - These line numbers are pre-Phase-A. After the split they live in the new files listed in the Phase B table.
- **Guard-test compatibility:** the source-scanning guard tests skip lines whose trimmed text starts with `//` (e.g. `tests/wa.domain.unit/Plans/PlanIdConsumerGuardTests.cs:39`), so `///` doc lines are safe. **Use only `///` doc comments, never `/** */`.** A block comment would expose literals like `"pro"` or `.PlanId` to the guards. `scripts/check-telemetry-pii.mjs` scans the text of src `.cs` files, so do not put sample logger calls (`.LogInformation(…)`) or raw email addresses in docs.

### 2.5 Enforcement — `src/Directory.Build.props`
- The file goes **inside src/, not at the repo root**, so it applies to the 5 src projects only. The test projects live under tests/ and do not inherit it (tests are exempt from rule 2).
- The Docker builds (`src/wa.api/Dockerfile`, `src/wa.api/Dockerfile.migrate`, `src/wa.workers/Dockerfile`) run `COPY src ./` into `/src`. The props file therefore lands at `/src/Directory.Build.props` and still applies to `/src/wa.api/…`. `.dockerignore` does not exclude it.
- The property group is conditioned on `.csproj`, so `src/wa.web/wa.web.esproj` is untouched.
- **Phase B step 0** creates it with `GenerateDocumentationFile=true` only. CS1591 and the other doc warnings then appear in every normal build while documenting.
- **Phase C** adds `WarningsAsErrors` for the doc diagnostics only: CS1570, CS1572, CS1573, CS1574, CS1587, CS1591.
- **No `TreatWarningsAsErrors`.** The 66 baseline warnings (CS0618 obsolete API, nullable) are out of scope and would break the build.
- Side effect: each src project now emits `<project>.xml` next to its dll (also in `dotnet publish` output). Harmless.
- Limit: when a member has no `<param>` tags at all, only StyleCop could flag it, and StyleCop is a new package (ADR + escalation; not done). The Reviewer checks `<param>`/`<returns>` completeness by sampling, on top of the Coder's self-check in Phase B.

### 2.6 Deliberately not done
- No `Commands/` / `Queries/` / `Handlers/` subfolders.
- No renaming of types, members, routes or namespaces. No un-nesting.
- No StyleCop or analyzer packages. No `.editorconfig` code-style rules beyond the IDE0130 silence.
- No `wa.web` changes. No docs/ edits by the Coder.

## 3. Implementation Plan

Execute the phases in order. **Each phase ends green and gets its own commit**, `refactor: CHORE-code-style <phase>`. Phase B may be split into B1–B4 commits, one per project.

Stage exact paths, or `git add src tests` limited to the phase's files. Never use `-A`: the repo root carries a stray untracked `Inkling-2-Mini.Modelfile`. Do not push.

**Per-phase gate** (PowerShell, repo root):
```powershell
dotnet build src/wa.slnx -t:Rebuild           # full rebuild so every warning is re-reported; 0 errors; unique-warning count as stated per phase
dotnet test tests/wa.domain.unit                # includes the source-scanning guard tests
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
node scripts/check-telemetry-pii.mjs; node scripts/check-telemetry-pii.mjs selftest
```
The slnx includes the 4 test projects, so the build also compiles tests/wa.api.integration.

**End of Phase C:** also run `dotnet test tests/wa.api.integration`. It uses Testcontainers, so check `docker version` first and start Docker Desktop if needed. It takes 15+ minutes; run it in the background. If Docker is unavailable, say so in the commit body and hand the integration run to the Reviewer/orchestrator. Never skip it silently.

### Phase A — one type per file + interface folders (structure only, no doc edits)
1. **wa.domain** (7 splits): follow the inventory table. Gate: build + `dotnet test tests/wa.domain.unit`.
2. **wa.infrastructure** (4 splits):
   - `DataExportStore.cs` keeps DataExportStore; create `DataExportJobStore.cs`.
   - `LimitsStores.cs` is deleted; create `PlansStore.cs` + `FlagsStore.cs`. `UserPlanStore.cs` already exists separately and is untouched.
   - `BlobSasMinter.cs` gives up `SasLogMask.cs`.
   - `UnsubscribeService.cs` gives up `UnsubscribeResult.cs`.
3. **wa.api** (1 split): move `ProblemFieldError` out of `src/wa.api/Middleware/BusinessException.cs`.
4. **wa.application** (77 splits + 24 `git mv`: 14 interface moves + 10 renames). Go folder by folder and build after each one: Limits → Ports → UseCases/Admin → Auth → Billing → DataExport → EmailSettings → Features → Gdpr → Health → Sample → Transfers.
   - A file whose name matches no remaining type is **deleted** after extraction (marked **DELETE** in the table), e.g. `Auth/MeProfile.cs`, `Auth/Login.cs`, `Ports/Events.cs`, `Ports/Health.cs`, `Limits/Caches.cs`, `Transfers/CreateDraft.cs`.
   - `Transfers/Errors.cs` holds only TransferSizeExceededException; `git mv` it to `TransferSizeExceededException.cs`.
5. **Guard-test allow-lists** (tests/wa.domain.unit). Allow-listed source paths move with their split. Apply the edits in §4 "Guard allow-list edits". Each new path was found by mapping every matching source line to the type that owns it after the split. Run `dotnet test tests/wa.domain.unit`. A hit on a new path means an entry is still missing: fix the allow-list path, never the regex.
6. **tests** (8 splits):
   - wa.api.integration: 6 files.
   - wa.application.unit: `RecordingLogger` (it is `internal sealed`; keep that).
   - wa.workers.unit: `WorkerServicesCollection`. It is an xUnit `[CollectionDefinition]`; collection binding survives a same-assembly, same-namespace move.
7. **Self-check:** re-run a one-type-per-file scan with a throwaway script outside the repo, and delete it afterwards. Expected result:
   - every non-Migrations `.cs` file has exactly one top-level type, and its name equals the file name;
   - under src/wa.application, `interface` files appear only in `Ports/` or `**/Interfaces/`;
   - unique build warnings = 66 (baseline).
8. Commit: `refactor: CHORE-code-style phase A — one type per file, interface folders`.

### Phase B — XML documentation (src only)
0. Create `src/Directory.Build.props` in its warning-only form (§4). `dotnet build src/wa.slnx -t:Rebuild` now shows about 709 CS1591 plus the malformed sites. The compiler output is the authoritative worklist; the Phase B table maps the sites to post-Phase-A paths.
1. Work project by project:

   | Step | Project | Files | Missing (CS1591) | Incomplete (about) |
   |---|---|---|---|---|
   | B1 | wa.domain | 51 | 195 | 48 |
   | B2 | wa.application | 231 | 316 | 143 |
   | B3 | wa.infrastructure | 76 | 161 | 43 |
   | B4 | wa.api + wa.workers | 21 | 37 | 9 |

   In each file, add or complete docs per §2.4. Also document by hand the internal types `SasLogMask` and `TransferTrackerExtensions` and every `internal` member (grep `^\s*internal ` under src). Fix the malformed sites as you reach them.
2. After each project: the build shows no CS15xx left for that project, and the per-phase gate is green. The guard tests and the PII script prove the docs did not trip a scanner.
3. Completeness self-check for every touched file:
   - every member with parameters has a `<param>` for each one;
   - every value-returning member has `<returns>` (or `<inheritdoc />`).

   A boilerplate-only doc is a review finding.
4. One commit per project: `refactor: CHORE-code-style phase B<n> — XML docs <project>`. At the end of Phase B, unique warnings = 66 and there are 0 CS15xx.

### Phase C — enforcement
1. Add the `WarningsAsErrors` line to `src/Directory.Build.props` (§4).
2. Create `src/.editorconfig` with the IDE0130 silence for `Interfaces/` folders (§4).
3. Prove the guard bites: temporarily delete one `<summary>` in a src file. `dotnet build src/wa.slnx` must fail with `error CS1591`. Revert it; `git diff` on that file must be empty.
4. Run the full gate, including tests/wa.api.integration. Commit: `refactor: CHORE-code-style phase C — doc diagnostics as errors`.
5. Optional, if Docker is up: `docker build -f src/wa.api/Dockerfile .` proves the props file is honoured inside the container publish.

## 4. Files for Coder

### FILES FOR CODER

**How to read this inventory.** It was generated from a brace-depth scan of every `.cs` file plus the compiler's CS1591 list.
- **MODIFY** marked **DELETE** = remove the file (`git rm`) once every type has been extracted.
- **MODIFY** with `git mv` = move or rename the file whole. Its **CREATE** row is the target path.
- **CREATE** = a new file holding exactly the named type, cut from the named source.
- **DOCS** rows (Phase B) are modifications of post-Phase-A paths. Many of those files only exist after Phase A, so they use the label DOCS instead of MODIFY. Counts come from the pre-Phase-A compiler run mapped to the owning type. Treat them as a guide: the compiler output is authoritative.
- Files not listed are not touched.

#### Phase A — tests/wa.domain.unit (guard allow-lists)

| File | Action | What to change |
|---|---|---|
| `tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs` | MODIFY | FeaturesProviderAllowList + PlanCodeComparisonAllowList paths, see "Guard allow-list edits" |
| `tests/wa.domain.unit/Plans/PlanIdConsumerGuardTests.cs` | MODIFY | AllowList paths, see "Guard allow-list edits" |
| `tests/wa.domain.unit/Plans/LimitsJsonConsumerGuardTests.cs` | MODIFY | AllowList path for GetFlagsQuery split |

#### Phase B/C — config

| File | Action | What to change |
|---|---|---|
| `src/Directory.Build.props` | CREATE | Phase B step 0: GenerateDocumentationFile. Phase C: add doc WarningsAsErrors (content below) |
| `src/.editorconfig` | CREATE | Phase C: IDE0130 silence for Interfaces folders (content below) |

#### Phase A — src/wa.api

| File | Action | What to change |
|---|---|---|
| `src/wa.api/Middleware/BusinessException.cs` | MODIFY | Split (2 types). keep `BusinessException` only; extract: record ProblemFieldError |
| `src/wa.api/Middleware/ProblemFieldError.cs` | CREATE | record `ProblemFieldError` from `BusinessException.cs` |

#### Phase A — src/wa.application

| File | Action | What to change |
|---|---|---|
| `src/wa.application/Limits/Caches.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: class PlansCache, class FlagsCache |
| `src/wa.application/Limits/PlansCache.cs` | CREATE | class `PlansCache` from `Caches.cs` |
| `src/wa.application/Limits/FlagsCache.cs` | CREATE | class `FlagsCache` from `Caches.cs` |
| `src/wa.application/Limits/FeaturesProvider.cs` | MODIFY | Split (2 types). keep `FeaturesProvider` only; extract: interface IFeaturesProvider |
| `src/wa.application/Limits/Interfaces/IFeaturesProvider.cs` | CREATE | interface `IFeaturesProvider` from `FeaturesProvider.cs` |
| `src/wa.application/Limits/LimitsProvider.cs` | MODIFY | Split (3 types). keep `LimitsProvider` only; extract: interface ILimitsProvider, interface IFlagResolver |
| `src/wa.application/Limits/Interfaces/ILimitsProvider.cs` | CREATE | interface `ILimitsProvider` from `LimitsProvider.cs` |
| `src/wa.application/Limits/Interfaces/IFlagResolver.cs` | CREATE | interface `IFlagResolver` from `LimitsProvider.cs` |
| `src/wa.application/Limits/PlanContext.cs` | MODIFY | Split (3 types). keep `PlanContext` only; extract: interface IPlanContextAccessor, class PlanContextAccessor |
| `src/wa.application/Limits/Interfaces/IPlanContextAccessor.cs` | CREATE | interface `IPlanContextAccessor` from `PlanContext.cs` |
| `src/wa.application/Limits/PlanContextAccessor.cs` | CREATE | class `PlanContextAccessor` from `PlanContext.cs` |
| `src/wa.application/Ports/BlobSasMinterPort.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: interface IBlobSasMinter, interface IExportSasMinter, interface IOrgLogoSasMinter |
| `src/wa.application/Ports/IBlobSasMinter.cs` | CREATE | interface `IBlobSasMinter` from `BlobSasMinterPort.cs` |
| `src/wa.application/Ports/IExportSasMinter.cs` | CREATE | interface `IExportSasMinter` from `BlobSasMinterPort.cs` |
| `src/wa.application/Ports/IOrgLogoSasMinter.cs` | CREATE | interface `IOrgLogoSasMinter` from `BlobSasMinterPort.cs` |
| `src/wa.application/Ports/BlobStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface IBlobStore, interface IBlobContentWriter |
| `src/wa.application/Ports/IBlobStore.cs` | CREATE | interface `IBlobStore` from `BlobStore.cs` |
| `src/wa.application/Ports/IBlobContentWriter.cs` | CREATE | interface `IBlobContentWriter` from `BlobStore.cs` |
| `src/wa.application/Ports/Events.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: class DomainEventRecord, interface IEventPublisher |
| `src/wa.application/Ports/DomainEventRecord.cs` | CREATE | class `DomainEventRecord` from `Events.cs` |
| `src/wa.application/Ports/IEventPublisher.cs` | CREATE | interface `IEventPublisher` from `Events.cs` |
| `src/wa.application/Ports/Health.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: enum ComponentStatus, interface IDbHealthProbe, interface ISbHealthProbe |
| `src/wa.application/Ports/ComponentStatus.cs` | CREATE | enum `ComponentStatus` from `Health.cs` |
| `src/wa.application/Ports/IDbHealthProbe.cs` | CREATE | interface `IDbHealthProbe` from `Health.cs` |
| `src/wa.application/Ports/ISbHealthProbe.cs` | CREATE | interface `ISbHealthProbe` from `Health.cs` |
| `src/wa.application/Ports/IEmailSender.cs` | MODIFY | Split (2 types). keep `IEmailSender` only; extract: record EmailMessage |
| `src/wa.application/Ports/EmailMessage.cs` | CREATE | record `EmailMessage` from `IEmailSender.cs` |
| `src/wa.application/Ports/IIdempotencyStore.cs` | MODIFY | Split (2 types). keep `IIdempotencyStore` only; extract: record IdempotencyEntry |
| `src/wa.application/Ports/IdempotencyEntry.cs` | CREATE | record `IdempotencyEntry` from `IIdempotencyStore.cs` |
| `src/wa.application/Ports/ITemplateRenderer.cs` | MODIFY | Split (3 types). keep `ITemplateRenderer` only; extract: record FileEntry, record TemplateData |
| `src/wa.application/Ports/FileEntry.cs` | CREATE | record `FileEntry` from `ITemplateRenderer.cs` |
| `src/wa.application/Ports/TemplateData.cs` | CREATE | record `TemplateData` from `ITemplateRenderer.cs` |
| `src/wa.application/Ports/LimitsStores.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: class PlanUnknownException, interface IPlansStore, interface IUserPlanStore, interface IFlagsStore |
| `src/wa.application/Ports/PlanUnknownException.cs` | CREATE | class `PlanUnknownException` from `LimitsStores.cs` |
| `src/wa.application/Ports/IPlansStore.cs` | CREATE | interface `IPlansStore` from `LimitsStores.cs` |
| `src/wa.application/Ports/IUserPlanStore.cs` | CREATE | interface `IUserPlanStore` from `LimitsStores.cs` |
| `src/wa.application/Ports/IFlagsStore.cs` | CREATE | interface `IFlagsStore` from `LimitsStores.cs` |
| `src/wa.application/UseCases/Admin/AdminDeleteTransferCommand.cs` | MODIFY | Split (2 types). keep `AdminDeleteTransferCommand` only; extract: class AdminDeleteTransferHandler |
| `src/wa.application/UseCases/Admin/AdminDeleteTransferHandler.cs` | CREATE | class `AdminDeleteTransferHandler` from `AdminDeleteTransferCommand.cs` |
| `src/wa.application/UseCases/Admin/AdminForceDeleteUserCommand.cs` | MODIFY | Split (3 types). keep `AdminForceDeleteUserCommand` only; extract: record AdminForceDeleteResult, class AdminForceDeleteUserHandler |
| `src/wa.application/UseCases/Admin/AdminForceDeleteResult.cs` | CREATE | record `AdminForceDeleteResult` from `AdminForceDeleteUserCommand.cs` |
| `src/wa.application/UseCases/Admin/AdminForceDeleteUserHandler.cs` | CREATE | class `AdminForceDeleteUserHandler` from `AdminForceDeleteUserCommand.cs` |
| `src/wa.application/UseCases/Admin/AdminRolePolicy.cs` | MODIFY | Split (2 types). keep `AdminRolePolicy` only; extract: enum AdminRole |
| `src/wa.application/UseCases/Admin/AdminRole.cs` | CREATE | enum `AdminRole` from `AdminRolePolicy.cs` |
| `src/wa.application/UseCases/Admin/AuditWriter.cs` | MODIFY | Split (2 types). keep `AuditWriter` only; extract: interface IAuditWriter |
| `src/wa.application/UseCases/Admin/Interfaces/IAuditWriter.cs` | CREATE | interface `IAuditWriter` from `AuditWriter.cs` |
| `src/wa.application/UseCases/Admin/GetErasuresQuery.cs` | MODIFY | Split (5 types). keep `GetErasuresQuery` only; extract: record ErasureListItem, record GetErasuresResult, class ErasureReportPolicy, class GetErasuresQueryHandler |
| `src/wa.application/UseCases/Admin/ErasureListItem.cs` | CREATE | record `ErasureListItem` from `GetErasuresQuery.cs` |
| `src/wa.application/UseCases/Admin/GetErasuresResult.cs` | CREATE | record `GetErasuresResult` from `GetErasuresQuery.cs` |
| `src/wa.application/UseCases/Admin/ErasureReportPolicy.cs` | CREATE | class `ErasureReportPolicy` from `GetErasuresQuery.cs` |
| `src/wa.application/UseCases/Admin/GetErasuresQueryHandler.cs` | CREATE | class `GetErasuresQueryHandler` from `GetErasuresQuery.cs` |
| `src/wa.application/UseCases/Admin/GetFlagsQuery.cs` | MODIFY | Split (3 types). keep `GetFlagsQuery` only; extract: record FlagView, class GetFlagsQueryHandler |
| `src/wa.application/UseCases/Admin/FlagView.cs` | CREATE | record `FlagView` from `GetFlagsQuery.cs` |
| `src/wa.application/UseCases/Admin/GetFlagsQueryHandler.cs` | CREATE | class `GetFlagsQueryHandler` from `GetFlagsQuery.cs` |
| `src/wa.application/UseCases/Admin/GetOverviewQuery.cs` | MODIFY | Split (3 types). keep `GetOverviewQuery` only; extract: record GetOverviewResult, class GetOverviewQueryHandler |
| `src/wa.application/UseCases/Admin/GetOverviewResult.cs` | CREATE | record `GetOverviewResult` from `GetOverviewQuery.cs` |
| `src/wa.application/UseCases/Admin/GetOverviewQueryHandler.cs` | CREATE | class `GetOverviewQueryHandler` from `GetOverviewQuery.cs` |
| `src/wa.application/UseCases/Admin/GetRecentAuditQuery.cs` | MODIFY | Split (2 types). keep `GetRecentAuditQuery` only; extract: class GetRecentAuditQueryHandler |
| `src/wa.application/UseCases/Admin/GetRecentAuditQueryHandler.cs` | CREATE | class `GetRecentAuditQueryHandler` from `GetRecentAuditQuery.cs` |
| `src/wa.application/UseCases/Admin/GetSuppressionsQuery.cs` | MODIFY | Split (2 types). keep `GetSuppressionsQuery` only; extract: class GetSuppressionsQueryHandler |
| `src/wa.application/UseCases/Admin/GetSuppressionsQueryHandler.cs` | CREATE | class `GetSuppressionsQueryHandler` from `GetSuppressionsQuery.cs` |
| `src/wa.application/UseCases/Admin/GetUsersQuery.cs` | MODIFY | Split (3 types). keep `GetUsersQuery` only; extract: record GetUsersResult, class GetUsersQueryHandler |
| `src/wa.application/UseCases/Admin/GetUsersResult.cs` | CREATE | record `GetUsersResult` from `GetUsersQuery.cs` |
| `src/wa.application/UseCases/Admin/GetUsersQueryHandler.cs` | CREATE | class `GetUsersQueryHandler` from `GetUsersQuery.cs` |
| `src/wa.application/UseCases/Admin/IAdminUserStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface IAdminUserStore, record AdminUserRow |
| `src/wa.application/UseCases/Admin/Interfaces/IAdminUserStore.cs` | CREATE | interface `IAdminUserStore` from `IAdminUserStore.cs` |
| `src/wa.application/UseCases/Admin/AdminUserRow.cs` | CREATE | record `AdminUserRow` from `IAdminUserStore.cs` |
| `src/wa.application/UseCases/Admin/IErasureReportStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: record ErasureReportRow, interface IErasureReportStore |
| `src/wa.application/UseCases/Admin/ErasureReportRow.cs` | CREATE | record `ErasureReportRow` from `IErasureReportStore.cs` |
| `src/wa.application/UseCases/Admin/Interfaces/IErasureReportStore.cs` | CREATE | interface `IErasureReportStore` from `IErasureReportStore.cs` |
| `src/wa.application/UseCases/Admin/IOverviewStore.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: interface IOverviewStore, record OverviewData, record RecentTransferItem |
| `src/wa.application/UseCases/Admin/Interfaces/IOverviewStore.cs` | CREATE | interface `IOverviewStore` from `IOverviewStore.cs` |
| `src/wa.application/UseCases/Admin/OverviewData.cs` | CREATE | record `OverviewData` from `IOverviewStore.cs` |
| `src/wa.application/UseCases/Admin/RecentTransferItem.cs` | CREATE | record `RecentTransferItem` from `IOverviewStore.cs` |
| `src/wa.application/UseCases/Admin/ISearchTransfersStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface ISearchTransfersStore, record AdminTransferRow |
| `src/wa.application/UseCases/Admin/Interfaces/ISearchTransfersStore.cs` | CREATE | interface `ISearchTransfersStore` from `ISearchTransfersStore.cs` |
| `src/wa.application/UseCases/Admin/AdminTransferRow.cs` | CREATE | record `AdminTransferRow` from `ISearchTransfersStore.cs` |
| `src/wa.application/UseCases/Admin/OverviewSnapshotCache.cs` | MODIFY | Split (2 types). keep `OverviewSnapshotCache` only; extract: record OverviewSnapshot |
| `src/wa.application/UseCases/Admin/OverviewSnapshot.cs` | CREATE | record `OverviewSnapshot` from `OverviewSnapshotCache.cs` |
| `src/wa.application/UseCases/Admin/RemoveSuppressionCommand.cs` | MODIFY | Split (2 types). keep `RemoveSuppressionCommand` only; extract: class RemoveSuppressionCommandHandler |
| `src/wa.application/UseCases/Admin/RemoveSuppressionCommandHandler.cs` | CREATE | class `RemoveSuppressionCommandHandler` from `RemoveSuppressionCommand.cs` |
| `src/wa.application/UseCases/Admin/SearchTransfersQuery.cs` | MODIFY | Split (4 types). keep `SearchTransfersQuery` only; extract: record SearchTransfersResult, class SearchTransfersValidationException, class SearchTransfersQueryHandler |
| `src/wa.application/UseCases/Admin/SearchTransfersResult.cs` | CREATE | record `SearchTransfersResult` from `SearchTransfersQuery.cs` |
| `src/wa.application/UseCases/Admin/SearchTransfersValidationException.cs` | CREATE | class `SearchTransfersValidationException` from `SearchTransfersQuery.cs` |
| `src/wa.application/UseCases/Admin/SearchTransfersQueryHandler.cs` | CREATE | class `SearchTransfersQueryHandler` from `SearchTransfersQuery.cs` |
| `src/wa.application/UseCases/Admin/SetFlagCommand.cs` | MODIFY | Split (4 types). keep `SetFlagCommand` only; extract: record SetFlagResult, class SetFlagException, class SetFlagCommandHandler |
| `src/wa.application/UseCases/Admin/SetFlagResult.cs` | CREATE | record `SetFlagResult` from `SetFlagCommand.cs` |
| `src/wa.application/UseCases/Admin/SetFlagException.cs` | CREATE | class `SetFlagException` from `SetFlagCommand.cs` |
| `src/wa.application/UseCases/Admin/SetFlagCommandHandler.cs` | CREATE | class `SetFlagCommandHandler` from `SetFlagCommand.cs` |
| `src/wa.application/UseCases/Admin/SetUserPlanCommand.cs` | MODIFY | Split (4 types). keep `SetUserPlanCommand` only; extract: record SetUserPlanResult, class SetUserPlanException, class SetUserPlanCommandHandler |
| `src/wa.application/UseCases/Admin/SetUserPlanResult.cs` | CREATE | record `SetUserPlanResult` from `SetUserPlanCommand.cs` |
| `src/wa.application/UseCases/Admin/SetUserPlanException.cs` | CREATE | class `SetUserPlanException` from `SetUserPlanCommand.cs` |
| `src/wa.application/UseCases/Admin/SetUserPlanCommandHandler.cs` | CREATE | class `SetUserPlanCommandHandler` from `SetUserPlanCommand.cs` |
| `src/wa.application/UseCases/Auth/ForgotPassword.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: record ForgotPasswordCommand, class ForgotPasswordHandler, record ForgotPasswordResult, class ForgotPasswordValidationException |
| `src/wa.application/UseCases/Auth/ForgotPasswordCommand.cs` | CREATE | record `ForgotPasswordCommand` from `ForgotPassword.cs` |
| `src/wa.application/UseCases/Auth/ForgotPasswordHandler.cs` | CREATE | class `ForgotPasswordHandler` from `ForgotPassword.cs` |
| `src/wa.application/UseCases/Auth/ForgotPasswordResult.cs` | CREATE | record `ForgotPasswordResult` from `ForgotPassword.cs` |
| `src/wa.application/UseCases/Auth/ForgotPasswordValidationException.cs` | CREATE | class `ForgotPasswordValidationException` from `ForgotPassword.cs` |
| `src/wa.application/UseCases/Auth/Login.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: record LoginCommand, record LoginResult, class LoginHandler, class LoginValidationException, class LoginWrongPasswordException |
| `src/wa.application/UseCases/Auth/LoginCommand.cs` | CREATE | record `LoginCommand` from `Login.cs` |
| `src/wa.application/UseCases/Auth/LoginResult.cs` | CREATE | record `LoginResult` from `Login.cs` |
| `src/wa.application/UseCases/Auth/LoginHandler.cs` | CREATE | class `LoginHandler` from `Login.cs` |
| `src/wa.application/UseCases/Auth/LoginValidationException.cs` | CREATE | class `LoginValidationException` from `Login.cs` |
| `src/wa.application/UseCases/Auth/LoginWrongPasswordException.cs` | CREATE | class `LoginWrongPasswordException` from `Login.cs` |
| `src/wa.application/UseCases/Auth/LogoContentSniffer.cs` | MODIFY | Split (2 types). keep `LogoContentSniffer` only; extract: enum LogoImageType |
| `src/wa.application/UseCases/Auth/LogoImageType.cs` | CREATE | enum `LogoImageType` from `LogoContentSniffer.cs` |
| `src/wa.application/UseCases/Auth/MeProfile.cs` | MODIFY | Split (12 types). **DELETE** (git rm) after extracting all types; extract: record GetProfileCommand, record UpdateProfileCommand, record struct OrgNamePatch, class OrgNamePatchJsonConverter, record DeleteAccountCommand, record UpdateProfileRequest, record GetProfileResult, record DeleteAccountResult, class MeProfileException, class GetProfileHandler, class UpdateProfileHandler, class DeleteAccountHandler |
| `src/wa.application/UseCases/Auth/GetProfileCommand.cs` | CREATE | record `GetProfileCommand` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/UpdateProfileCommand.cs` | CREATE | record `UpdateProfileCommand` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/OrgNamePatch.cs` | CREATE | record struct `OrgNamePatch` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/OrgNamePatchJsonConverter.cs` | CREATE | class `OrgNamePatchJsonConverter` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/DeleteAccountCommand.cs` | CREATE | record `DeleteAccountCommand` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/UpdateProfileRequest.cs` | CREATE | record `UpdateProfileRequest` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/GetProfileResult.cs` | CREATE | record `GetProfileResult` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/DeleteAccountResult.cs` | CREATE | record `DeleteAccountResult` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/MeProfileException.cs` | CREATE | class `MeProfileException` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/GetProfileHandler.cs` | CREATE | class `GetProfileHandler` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/UpdateProfileHandler.cs` | CREATE | class `UpdateProfileHandler` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/DeleteAccountHandler.cs` | CREATE | class `DeleteAccountHandler` from `MeProfile.cs` |
| `src/wa.application/UseCases/Auth/OrgLogo.cs` | MODIFY | Split (8 types). **DELETE** (git rm) after extracting all types; extract: record RemoveOrgLogoCommand, class RemoveOrgLogoHandler, record OrgLogoView, record UploadOrgLogoCommand, record GetOrgLogoQuery, class OrgLogoRejectedException, class UploadOrgLogoHandler, class GetOrgLogoHandler |
| `src/wa.application/UseCases/Auth/RemoveOrgLogoCommand.cs` | CREATE | record `RemoveOrgLogoCommand` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/RemoveOrgLogoHandler.cs` | CREATE | class `RemoveOrgLogoHandler` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/OrgLogoView.cs` | CREATE | record `OrgLogoView` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/UploadOrgLogoCommand.cs` | CREATE | record `UploadOrgLogoCommand` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/GetOrgLogoQuery.cs` | CREATE | record `GetOrgLogoQuery` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/OrgLogoRejectedException.cs` | CREATE | class `OrgLogoRejectedException` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/UploadOrgLogoHandler.cs` | CREATE | class `UploadOrgLogoHandler` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/GetOrgLogoHandler.cs` | CREATE | class `GetOrgLogoHandler` from `OrgLogo.cs` |
| `src/wa.application/UseCases/Auth/RedeemMagicLink.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: record RedeemMagicLinkCommand, class RedeemMagicLinkHandler, class LinkExpiredException, class RedeemValidationException, record RedeemMagicLinkResult |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkCommand.cs` | CREATE | record `RedeemMagicLinkCommand` from `RedeemMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkHandler.cs` | CREATE | class `RedeemMagicLinkHandler` from `RedeemMagicLink.cs` |
| `src/wa.application/UseCases/Auth/LinkExpiredException.cs` | CREATE | class `LinkExpiredException` from `RedeemMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RedeemValidationException.cs` | CREATE | class `RedeemValidationException` from `RedeemMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkResult.cs` | CREATE | record `RedeemMagicLinkResult` from `RedeemMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RequestMagicLink.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: record RequestMagicLinkCommand, class RequestMagicLinkHandler, record RequestMagicLinkResult, class RequestMagicLinkValidationException |
| `src/wa.application/UseCases/Auth/RequestMagicLinkCommand.cs` | CREATE | record `RequestMagicLinkCommand` from `RequestMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RequestMagicLinkHandler.cs` | CREATE | class `RequestMagicLinkHandler` from `RequestMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RequestMagicLinkResult.cs` | CREATE | record `RequestMagicLinkResult` from `RequestMagicLink.cs` |
| `src/wa.application/UseCases/Auth/RequestMagicLinkValidationException.cs` | CREATE | class `RequestMagicLinkValidationException` from `RequestMagicLink.cs` |
| `src/wa.application/UseCases/Auth/Signup.cs` | MODIFY | Split (6 types). **DELETE** (git rm) after extracting all types; extract: record SignupCommand, record SignupResult, class SignupHandler, class SignupValidationException, interface ISessionTokenMinter, class SessionTokenService |
| `src/wa.application/UseCases/Auth/SignupCommand.cs` | CREATE | record `SignupCommand` from `Signup.cs` |
| `src/wa.application/UseCases/Auth/SignupResult.cs` | CREATE | record `SignupResult` from `Signup.cs` |
| `src/wa.application/UseCases/Auth/SignupHandler.cs` | CREATE | class `SignupHandler` from `Signup.cs` |
| `src/wa.application/UseCases/Auth/SignupValidationException.cs` | CREATE | class `SignupValidationException` from `Signup.cs` |
| `src/wa.application/UseCases/Auth/Interfaces/ISessionTokenMinter.cs` | CREATE | interface `ISessionTokenMinter` from `Signup.cs` |
| `src/wa.application/UseCases/Auth/SessionTokenService.cs` | CREATE | class `SessionTokenService` from `Signup.cs` |
| `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` | MODIFY | Split (4 types). keep `CreateCheckoutSessionCommand` only; extract: record CheckoutRequest, class CheckoutException, class CreateCheckoutSessionHandler |
| `src/wa.application/UseCases/Billing/CheckoutRequest.cs` | CREATE | record `CheckoutRequest` from `CreateCheckoutSessionCommand.cs` |
| `src/wa.application/UseCases/Billing/CheckoutException.cs` | CREATE | class `CheckoutException` from `CreateCheckoutSessionCommand.cs` |
| `src/wa.application/UseCases/Billing/CreateCheckoutSessionHandler.cs` | CREATE | class `CreateCheckoutSessionHandler` from `CreateCheckoutSessionCommand.cs` |
| `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs` | MODIFY | Split (3 types). keep `GetPlanUsageQuery` only; extract: record PlanUsageResult, class GetPlanUsageHandler |
| `src/wa.application/UseCases/Billing/PlanUsageResult.cs` | CREATE | record `PlanUsageResult` from `GetPlanUsageQuery.cs` |
| `src/wa.application/UseCases/Billing/GetPlanUsageHandler.cs` | CREATE | class `GetPlanUsageHandler` from `GetPlanUsageQuery.cs` |
| `src/wa.application/UseCases/Billing/GetPortalSessionQuery.cs` | MODIFY | Split (3 types). keep `GetPortalSessionQuery` only; extract: record PortalSessionResult, class GetPortalSessionHandler |
| `src/wa.application/UseCases/Billing/PortalSessionResult.cs` | CREATE | record `PortalSessionResult` from `GetPortalSessionQuery.cs` |
| `src/wa.application/UseCases/Billing/GetPortalSessionHandler.cs` | CREATE | class `GetPortalSessionHandler` from `GetPortalSessionQuery.cs` |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | MODIFY | Split (4 types). keep `HandleStripeWebhookCommand` only; extract: enum StripeWebhookOutcome, class StripeWebhookException, class HandleStripeWebhookHandler |
| `src/wa.application/UseCases/Billing/StripeWebhookOutcome.cs` | CREATE | enum `StripeWebhookOutcome` from `HandleStripeWebhookCommand.cs` |
| `src/wa.application/UseCases/Billing/StripeWebhookException.cs` | CREATE | class `StripeWebhookException` from `HandleStripeWebhookCommand.cs` |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookHandler.cs` | CREATE | class `HandleStripeWebhookHandler` from `HandleStripeWebhookCommand.cs` |
| `src/wa.application/UseCases/Billing/IBillingGraceStore.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: record GraceCandidate, enum GraceDowngradeOutcome, record GraceDowngradeResult, interface IBillingGraceStore |
| `src/wa.application/UseCases/Billing/GraceCandidate.cs` | CREATE | record `GraceCandidate` from `IBillingGraceStore.cs` |
| `src/wa.application/UseCases/Billing/GraceDowngradeOutcome.cs` | CREATE | enum `GraceDowngradeOutcome` from `IBillingGraceStore.cs` |
| `src/wa.application/UseCases/Billing/GraceDowngradeResult.cs` | CREATE | record `GraceDowngradeResult` from `IBillingGraceStore.cs` |
| `src/wa.application/UseCases/Billing/Interfaces/IBillingGraceStore.cs` | CREATE | interface `IBillingGraceStore` from `IBillingGraceStore.cs` |
| `src/wa.application/UseCases/Billing/IBillingSequenceStore.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: enum PaymentFailedOutcome, record PaymentFailedResult, enum InvoicePaidOutcome, record BillingSequenceRow, interface IBillingSequenceStore |
| `src/wa.application/UseCases/Billing/PaymentFailedOutcome.cs` | CREATE | enum `PaymentFailedOutcome` from `IBillingSequenceStore.cs` |
| `src/wa.application/UseCases/Billing/PaymentFailedResult.cs` | CREATE | record `PaymentFailedResult` from `IBillingSequenceStore.cs` |
| `src/wa.application/UseCases/Billing/InvoicePaidOutcome.cs` | CREATE | enum `InvoicePaidOutcome` from `IBillingSequenceStore.cs` |
| `src/wa.application/UseCases/Billing/BillingSequenceRow.cs` | CREATE | record `BillingSequenceRow` from `IBillingSequenceStore.cs` |
| `src/wa.application/UseCases/Billing/Interfaces/IBillingSequenceStore.cs` | CREATE | interface `IBillingSequenceStore` from `IBillingSequenceStore.cs` |
| `src/wa.application/UseCases/Billing/IStripeBillingStore.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: record StripeSubscriptionChange, enum StripeApplyOutcome, record StripeApplyResult, interface IStripeBillingStore |
| `src/wa.application/UseCases/Billing/StripeSubscriptionChange.cs` | CREATE | record `StripeSubscriptionChange` from `IStripeBillingStore.cs` |
| `src/wa.application/UseCases/Billing/StripeApplyOutcome.cs` | CREATE | enum `StripeApplyOutcome` from `IStripeBillingStore.cs` |
| `src/wa.application/UseCases/Billing/StripeApplyResult.cs` | CREATE | record `StripeApplyResult` from `IStripeBillingStore.cs` |
| `src/wa.application/UseCases/Billing/Interfaces/IStripeBillingStore.cs` | CREATE | interface `IStripeBillingStore` from `IStripeBillingStore.cs` |
| `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: record StripeCheckoutRequest, record StripeCheckoutSession, record StripePortalSession, interface IStripeCheckoutGateway, class BillingUnavailableException |
| `src/wa.application/UseCases/Billing/StripeCheckoutRequest.cs` | CREATE | record `StripeCheckoutRequest` from `IStripeCheckoutGateway.cs` |
| `src/wa.application/UseCases/Billing/StripeCheckoutSession.cs` | CREATE | record `StripeCheckoutSession` from `IStripeCheckoutGateway.cs` |
| `src/wa.application/UseCases/Billing/StripePortalSession.cs` | CREATE | record `StripePortalSession` from `IStripeCheckoutGateway.cs` |
| `src/wa.application/UseCases/Billing/Interfaces/IStripeCheckoutGateway.cs` | CREATE | interface `IStripeCheckoutGateway` from `IStripeCheckoutGateway.cs` |
| `src/wa.application/UseCases/Billing/BillingUnavailableException.cs` | CREATE | class `BillingUnavailableException` from `IStripeCheckoutGateway.cs` |
| `src/wa.application/UseCases/Billing/IStripeSubscriptionSource.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: record StripeSubscriptionSnapshot, interface IStripeSubscriptionSource |
| `src/wa.application/UseCases/Billing/StripeSubscriptionSnapshot.cs` | CREATE | record `StripeSubscriptionSnapshot` from `IStripeSubscriptionSource.cs` |
| `src/wa.application/UseCases/Billing/Interfaces/IStripeSubscriptionSource.cs` | CREATE | interface `IStripeSubscriptionSource` from `IStripeSubscriptionSource.cs` |
| `src/wa.application/UseCases/DataExport/ExportMyData.cs` | MODIFY | Split (7 types). **DELETE** (git rm) after extracting all types; extract: record ExportMyDataCommand, record GetDataExportQuery, record DataExportView, class DataExportPolicy, class DataExportInFlightException, class ExportMyDataHandler, class GetDataExportHandler |
| `src/wa.application/UseCases/DataExport/ExportMyDataCommand.cs` | CREATE | record `ExportMyDataCommand` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/GetDataExportQuery.cs` | CREATE | record `GetDataExportQuery` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/DataExportView.cs` | CREATE | record `DataExportView` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/DataExportPolicy.cs` | CREATE | class `DataExportPolicy` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/DataExportInFlightException.cs` | CREATE | class `DataExportInFlightException` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/ExportMyDataHandler.cs` | CREATE | class `ExportMyDataHandler` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/GetDataExportHandler.cs` | CREATE | class `GetDataExportHandler` from `ExportMyData.cs` |
| `src/wa.application/UseCases/DataExport/IDataExportStore.cs` | MODIFY | Split (7 types). **DELETE** (git rm) after extracting all types; extract: interface IDataExportStore, interface IDataExportJobStore, record PurgeableExport, record ExportProfile, record ExportFile, record ExportTransfer, record ExportSnapshot |
| `src/wa.application/UseCases/DataExport/Interfaces/IDataExportStore.cs` | CREATE | interface `IDataExportStore` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/DataExport/Interfaces/IDataExportJobStore.cs` | CREATE | interface `IDataExportJobStore` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/DataExport/PurgeableExport.cs` | CREATE | record `PurgeableExport` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/DataExport/ExportProfile.cs` | CREATE | record `ExportProfile` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/DataExport/ExportFile.cs` | CREATE | record `ExportFile` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/DataExport/ExportTransfer.cs` | CREATE | record `ExportTransfer` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/DataExport/ExportSnapshot.cs` | CREATE | record `ExportSnapshot` from `IDataExportStore.cs` |
| `src/wa.application/UseCases/Features/GetFeatureGatesQuery.cs` | MODIFY | Split (2 types). keep `GetFeatureGatesQuery` only; extract: class GetFeatureGatesQueryHandler |
| `src/wa.application/UseCases/Features/GetFeatureGatesQueryHandler.cs` | CREATE | class `GetFeatureGatesQueryHandler` from `GetFeatureGatesQuery.cs` |
| `src/wa.application/UseCases/Gdpr/IGdprSweepStore.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: interface IGdprSweepStore, interface ITelemetryEraser, class TelemetryUnavailableException, class GdprSweepPolicy |
| `src/wa.application/UseCases/Gdpr/Interfaces/IGdprSweepStore.cs` | CREATE | interface `IGdprSweepStore` from `IGdprSweepStore.cs` |
| `src/wa.application/UseCases/Gdpr/Interfaces/ITelemetryEraser.cs` | CREATE | interface `ITelemetryEraser` from `IGdprSweepStore.cs` |
| `src/wa.application/UseCases/Gdpr/TelemetryUnavailableException.cs` | CREATE | class `TelemetryUnavailableException` from `IGdprSweepStore.cs` |
| `src/wa.application/UseCases/Gdpr/GdprSweepPolicy.cs` | CREATE | class `GdprSweepPolicy` from `IGdprSweepStore.cs` |
| `src/wa.application/UseCases/Health/Health.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: record HealthQuery, record HealthResult, class HealthQueryHandler |
| `src/wa.application/UseCases/Health/HealthQuery.cs` | CREATE | record `HealthQuery` from `Health.cs` |
| `src/wa.application/UseCases/Health/HealthResult.cs` | CREATE | record `HealthResult` from `Health.cs` |
| `src/wa.application/UseCases/Health/HealthQueryHandler.cs` | CREATE | class `HealthQueryHandler` from `Health.cs` |
| `src/wa.application/UseCases/Sample/Ping.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: record PingCommand, class PingCommandHandler |
| `src/wa.application/UseCases/Sample/PingCommand.cs` | CREATE | record `PingCommand` from `Ping.cs` |
| `src/wa.application/UseCases/Sample/PingCommandHandler.cs` | CREATE | class `PingCommandHandler` from `Ping.cs` |
| `src/wa.application/UseCases/Transfers/CreateDraft.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: class CreateDraftFile, record CreateDraftCommand, record DraftFile, record CreateDraftResult, class CreateDraftHandler |
| `src/wa.application/UseCases/Transfers/CreateDraftFile.cs` | CREATE | class `CreateDraftFile` from `CreateDraft.cs` |
| `src/wa.application/UseCases/Transfers/CreateDraftCommand.cs` | CREATE | record `CreateDraftCommand` from `CreateDraft.cs` |
| `src/wa.application/UseCases/Transfers/DraftFile.cs` | CREATE | record `DraftFile` from `CreateDraft.cs` |
| `src/wa.application/UseCases/Transfers/CreateDraftResult.cs` | CREATE | record `CreateDraftResult` from `CreateDraft.cs` |
| `src/wa.application/UseCases/Transfers/CreateDraftHandler.cs` | CREATE | class `CreateDraftHandler` from `CreateDraft.cs` |
| `src/wa.application/UseCases/Transfers/DeleteTransferCommand.cs` | MODIFY | Split (3 types). keep `DeleteTransferCommand` only; extract: record MarkDeleteResult, class DeleteTransferHandler |
| `src/wa.application/UseCases/Transfers/MarkDeleteResult.cs` | CREATE | record `MarkDeleteResult` from `DeleteTransferCommand.cs` |
| `src/wa.application/UseCases/Transfers/DeleteTransferHandler.cs` | CREATE | class `DeleteTransferHandler` from `DeleteTransferCommand.cs` |
| `src/wa.application/UseCases/Transfers/FinalizeTransfer.cs` | MODIFY | Split (8 types). **DELETE** (git rm) after extracting all types; extract: class FinalizeFile, record FinalizeTransferCommand, record FinalizedFile, record FinalizeTransferResult, class FinalizeTransferHandler, class StorageQuotaExceededException, class ActiveTransfersExceededException, class BlobSizeMismatchException |
| `src/wa.application/UseCases/Transfers/FinalizeFile.cs` | CREATE | class `FinalizeFile` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/FinalizeTransferCommand.cs` | CREATE | record `FinalizeTransferCommand` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/FinalizedFile.cs` | CREATE | record `FinalizedFile` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/FinalizeTransferResult.cs` | CREATE | record `FinalizeTransferResult` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/FinalizeTransferHandler.cs` | CREATE | class `FinalizeTransferHandler` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/StorageQuotaExceededException.cs` | CREATE | class `StorageQuotaExceededException` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ActiveTransfersExceededException.cs` | CREATE | class `ActiveTransfersExceededException` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/BlobSizeMismatchException.cs` | CREATE | class `BlobSizeMismatchException` from `FinalizeTransfer.cs` |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlQuery.cs` | MODIFY | Split (4 types). keep `GetDownloadAllUrlQuery` only; extract: enum DownloadAllState, record DownloadAllResult, class GetDownloadAllUrlHandler |
| `src/wa.application/UseCases/Transfers/DownloadAllState.cs` | CREATE | enum `DownloadAllState` from `GetDownloadAllUrlQuery.cs` |
| `src/wa.application/UseCases/Transfers/DownloadAllResult.cs` | CREATE | record `DownloadAllResult` from `GetDownloadAllUrlQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs` | CREATE | class `GetDownloadAllUrlHandler` from `GetDownloadAllUrlQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetFileDownloadUrlQuery.cs` | MODIFY | Split (4 types). keep `GetFileDownloadUrlQuery` only; extract: enum FileDownloadState, record FileDownloadResult, class GetFileDownloadUrlHandler |
| `src/wa.application/UseCases/Transfers/FileDownloadState.cs` | CREATE | enum `FileDownloadState` from `GetFileDownloadUrlQuery.cs` |
| `src/wa.application/UseCases/Transfers/FileDownloadResult.cs` | CREATE | record `FileDownloadResult` from `GetFileDownloadUrlQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetFileDownloadUrlHandler.cs` | CREATE | class `GetFileDownloadUrlHandler` from `GetFileDownloadUrlQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetMyTransfer.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: record GetMyTransferQuery, record GetMyTransferFile, record GetMyTransferResult, class GetMyTransferHandler |
| `src/wa.application/UseCases/Transfers/GetMyTransferQuery.cs` | CREATE | record `GetMyTransferQuery` from `GetMyTransfer.cs` |
| `src/wa.application/UseCases/Transfers/GetMyTransferFile.cs` | CREATE | record `GetMyTransferFile` from `GetMyTransfer.cs` |
| `src/wa.application/UseCases/Transfers/GetMyTransferResult.cs` | CREATE | record `GetMyTransferResult` from `GetMyTransfer.cs` |
| `src/wa.application/UseCases/Transfers/GetMyTransferHandler.cs` | CREATE | class `GetMyTransferHandler` from `GetMyTransfer.cs` |
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | MODIFY | Split (5 types). keep `GetPublicTransferQuery` only; extract: record PublicFileItem, enum PublicTransferState, record GetPublicTransferResult, class GetPublicTransferHandler |
| `src/wa.application/UseCases/Transfers/PublicFileItem.cs` | CREATE | record `PublicFileItem` from `GetPublicTransferQuery.cs` |
| `src/wa.application/UseCases/Transfers/PublicTransferState.cs` | CREATE | enum `PublicTransferState` from `GetPublicTransferQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetPublicTransferResult.cs` | CREATE | record `GetPublicTransferResult` from `GetPublicTransferQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetPublicTransferHandler.cs` | CREATE | class `GetPublicTransferHandler` from `GetPublicTransferQuery.cs` |
| `src/wa.application/UseCases/Transfers/GetTransferAnalytics.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: record GetTransferAnalyticsQuery, class AnalyticsForbiddenException, class GetTransferAnalyticsHandler |
| `src/wa.application/UseCases/Transfers/GetTransferAnalyticsQuery.cs` | CREATE | record `GetTransferAnalyticsQuery` from `GetTransferAnalytics.cs` |
| `src/wa.application/UseCases/Transfers/AnalyticsForbiddenException.cs` | CREATE | class `AnalyticsForbiddenException` from `GetTransferAnalytics.cs` |
| `src/wa.application/UseCases/Transfers/GetTransferAnalyticsHandler.cs` | CREATE | class `GetTransferAnalyticsHandler` from `GetTransferAnalytics.cs` |
| `src/wa.application/UseCases/Transfers/IBlobDeletionStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface IBlobDeletionStore, record BlobDeletionBatchRow |
| `src/wa.application/UseCases/Transfers/Interfaces/IBlobDeletionStore.cs` | CREATE | interface `IBlobDeletionStore` from `IBlobDeletionStore.cs` |
| `src/wa.application/UseCases/Transfers/BlobDeletionBatchRow.cs` | CREATE | record `BlobDeletionBatchRow` from `IBlobDeletionStore.cs` |
| `src/wa.application/UseCases/Transfers/IEmailStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: record EmailTransferContext, interface IEmailStore |
| `src/wa.application/UseCases/Transfers/EmailTransferContext.cs` | CREATE | record `EmailTransferContext` from `IEmailStore.cs` |
| `src/wa.application/UseCases/Transfers/Interfaces/IEmailStore.cs` | CREATE | interface `IEmailStore` from `IEmailStore.cs` |
| `src/wa.application/UseCases/Transfers/IExpiryDeletionStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface IExpiryDeletionStore, record TransferDeletionBatchRow |
| `src/wa.application/UseCases/Transfers/Interfaces/IExpiryDeletionStore.cs` | CREATE | interface `IExpiryDeletionStore` from `IExpiryDeletionStore.cs` |
| `src/wa.application/UseCases/Transfers/TransferDeletionBatchRow.cs` | CREATE | record `TransferDeletionBatchRow` from `IExpiryDeletionStore.cs` |
| `src/wa.application/UseCases/Transfers/IExpiryStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface IExpiryStore, record ExpiryBatchRow |
| `src/wa.application/UseCases/Transfers/Interfaces/IExpiryStore.cs` | CREATE | interface `IExpiryStore` from `IExpiryStore.cs` |
| `src/wa.application/UseCases/Transfers/ExpiryBatchRow.cs` | CREATE | record `ExpiryBatchRow` from `IExpiryStore.cs` |
| `src/wa.application/UseCases/Transfers/IPublicTransferStore.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: interface IPublicTransferStore, enum SingleFileDownloadOutcome, record SingleFileDownloadResult |
| `src/wa.application/UseCases/Transfers/Interfaces/IPublicTransferStore.cs` | CREATE | interface `IPublicTransferStore` from `IPublicTransferStore.cs` |
| `src/wa.application/UseCases/Transfers/SingleFileDownloadOutcome.cs` | CREATE | enum `SingleFileDownloadOutcome` from `IPublicTransferStore.cs` |
| `src/wa.application/UseCases/Transfers/SingleFileDownloadResult.cs` | CREATE | record `SingleFileDownloadResult` from `IPublicTransferStore.cs` |
| `src/wa.application/UseCases/Transfers/IScheduledSendStore.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: interface IScheduledSendStore, record DueScheduledRow, record ActivatedTransfer |
| `src/wa.application/UseCases/Transfers/Interfaces/IScheduledSendStore.cs` | CREATE | interface `IScheduledSendStore` from `IScheduledSendStore.cs` |
| `src/wa.application/UseCases/Transfers/DueScheduledRow.cs` | CREATE | record `DueScheduledRow` from `IScheduledSendStore.cs` |
| `src/wa.application/UseCases/Transfers/ActivatedTransfer.cs` | CREATE | record `ActivatedTransfer` from `IScheduledSendStore.cs` |
| `src/wa.application/UseCases/Transfers/ISenderBrandingStore.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: record SenderBrandingRow, interface ISenderBrandingStore |
| `src/wa.application/UseCases/Transfers/SenderBrandingRow.cs` | CREATE | record `SenderBrandingRow` from `ISenderBrandingStore.cs` |
| `src/wa.application/UseCases/Transfers/Interfaces/ISenderBrandingStore.cs` | CREATE | interface `ISenderBrandingStore` from `ISenderBrandingStore.cs` |
| `src/wa.application/UseCases/Transfers/ITransferAnalyticsStore.cs` | MODIFY | Split (3 types). **DELETE** (git rm) after extracting all types; extract: interface ITransferAnalyticsStore, record AnalyticsTransfer, record AnalyticsAggregate |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferAnalyticsStore.cs` | CREATE | interface `ITransferAnalyticsStore` from `ITransferAnalyticsStore.cs` |
| `src/wa.application/UseCases/Transfers/AnalyticsTransfer.cs` | CREATE | record `AnalyticsTransfer` from `ITransferAnalyticsStore.cs` |
| `src/wa.application/UseCases/Transfers/AnalyticsAggregate.cs` | CREATE | record `AnalyticsAggregate` from `ITransferAnalyticsStore.cs` |
| `src/wa.application/UseCases/Transfers/ListMyTransfers.cs` | MODIFY | Split (4 types). **DELETE** (git rm) after extracting all types; extract: record ListMyTransfersQuery, record MyTransferRow, record ListMyTransfersResult, class ListMyTransfersHandler |
| `src/wa.application/UseCases/Transfers/ListMyTransfersQuery.cs` | CREATE | record `ListMyTransfersQuery` from `ListMyTransfers.cs` |
| `src/wa.application/UseCases/Transfers/MyTransferRow.cs` | CREATE | record `MyTransferRow` from `ListMyTransfers.cs` |
| `src/wa.application/UseCases/Transfers/ListMyTransfersResult.cs` | CREATE | record `ListMyTransfersResult` from `ListMyTransfers.cs` |
| `src/wa.application/UseCases/Transfers/ListMyTransfersHandler.cs` | CREATE | class `ListMyTransfersHandler` from `ListMyTransfers.cs` |
| `src/wa.application/UseCases/Transfers/ResendTransfer.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: record ResendTransferCommand, record ResendFileItem, record ResendTransferResult, class ResendTransferHandler, class ResendForbiddenException |
| `src/wa.application/UseCases/Transfers/ResendTransferCommand.cs` | CREATE | record `ResendTransferCommand` from `ResendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ResendFileItem.cs` | CREATE | record `ResendFileItem` from `ResendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ResendTransferResult.cs` | CREATE | record `ResendTransferResult` from `ResendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ResendTransferHandler.cs` | CREATE | class `ResendTransferHandler` from `ResendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ResendForbiddenException.cs` | CREATE | class `ResendForbiddenException` from `ResendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/SearchMyTransfers.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: record SearchMyTransfersQuery, class SearchMyTransfersHandler |
| `src/wa.application/UseCases/Transfers/SearchMyTransfersQuery.cs` | CREATE | record `SearchMyTransfersQuery` from `SearchMyTransfers.cs` |
| `src/wa.application/UseCases/Transfers/SearchMyTransfersHandler.cs` | CREATE | class `SearchMyTransfersHandler` from `SearchMyTransfers.cs` |
| `src/wa.application/UseCases/Transfers/SendTransfer.cs` | MODIFY | Split (9 types). **DELETE** (git rm) after extracting all types; extract: record SendTransferCommand, record SendTransferResult, interface ISendStore, record FileItemRow, class SendTransferHandler, class TransferValidationException, class ScheduleForbiddenException, class ScheduleOutOfWindowException, class FilesGoneException |
| `src/wa.application/UseCases/Transfers/SendTransferCommand.cs` | CREATE | record `SendTransferCommand` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/SendTransferResult.cs` | CREATE | record `SendTransferResult` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/Interfaces/ISendStore.cs` | CREATE | interface `ISendStore` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/FileItemRow.cs` | CREATE | record `FileItemRow` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/SendTransferHandler.cs` | CREATE | class `SendTransferHandler` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/TransferValidationException.cs` | CREATE | class `TransferValidationException` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ScheduleForbiddenException.cs` | CREATE | class `ScheduleForbiddenException` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/ScheduleOutOfWindowException.cs` | CREATE | class `ScheduleOutOfWindowException` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/FilesGoneException.cs` | CREATE | class `FilesGoneException` from `SendTransfer.cs` |
| `src/wa.application/UseCases/Transfers/SenderBranding.cs` | MODIFY | Split (2 types). keep `SenderBranding` only; extract: record SenderBrandingView |
| `src/wa.application/UseCases/Transfers/SenderBrandingView.cs` | CREATE | record `SenderBrandingView` from `SenderBranding.cs` |
| `src/wa.application/UseCases/Transfers/UnlockToken.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: interface IUnlockTokenMinter, class UnlockTokenService |
| `src/wa.application/UseCases/Transfers/Interfaces/IUnlockTokenMinter.cs` | CREATE | interface `IUnlockTokenMinter` from `UnlockToken.cs` |
| `src/wa.application/UseCases/Transfers/UnlockTokenService.cs` | CREATE | class `UnlockTokenService` from `UnlockToken.cs` |
| `src/wa.application/UseCases/Transfers/UnlockTransfer.cs` | MODIFY | Split (5 types). **DELETE** (git rm) after extracting all types; extract: record UnlockTransferCommand, enum UnlockState, record UnlockTransferResult, class UnlockTransferHandler, class UnlockPasswordMissingException |
| `src/wa.application/UseCases/Transfers/UnlockTransferCommand.cs` | CREATE | record `UnlockTransferCommand` from `UnlockTransfer.cs` |
| `src/wa.application/UseCases/Transfers/UnlockState.cs` | CREATE | enum `UnlockState` from `UnlockTransfer.cs` |
| `src/wa.application/UseCases/Transfers/UnlockTransferResult.cs` | CREATE | record `UnlockTransferResult` from `UnlockTransfer.cs` |
| `src/wa.application/UseCases/Transfers/UnlockTransferHandler.cs` | CREATE | class `UnlockTransferHandler` from `UnlockTransfer.cs` |
| `src/wa.application/UseCases/Transfers/UnlockPasswordMissingException.cs` | CREATE | class `UnlockPasswordMissingException` from `UnlockTransfer.cs` |
| `src/wa.application/UseCases/Admin/IAdminFlagStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Admin/Interfaces/IAdminFlagStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Admin/Interfaces/IAdminFlagStore.cs` | CREATE | target of git mv of `IAdminFlagStore.cs` (interface `IAdminFlagStore`) |
| `src/wa.application/UseCases/Admin/IAdminSuppressionStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Admin/Interfaces/IAdminSuppressionStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Admin/Interfaces/IAdminSuppressionStore.cs` | CREATE | target of git mv of `IAdminSuppressionStore.cs` (interface `IAdminSuppressionStore`) |
| `src/wa.application/UseCases/Admin/IAdminTransferStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Admin/Interfaces/IAdminTransferStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Admin/Interfaces/IAdminTransferStore.cs` | CREATE | target of git mv of `IAdminTransferStore.cs` (interface `IAdminTransferStore`) |
| `src/wa.application/UseCases/Admin/IAuditReadStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Admin/Interfaces/IAuditReadStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Admin/Interfaces/IAuditReadStore.cs` | CREATE | target of git mv of `IAuditReadStore.cs` (interface `IAuditReadStore`) |
| `src/wa.application/UseCases/Admin/IAuditStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Admin/Interfaces/IAuditStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Admin/Interfaces/IAuditStore.cs` | CREATE | target of git mv of `IAuditStore.cs` (interface `IAuditStore`) |
| `src/wa.application/UseCases/Auth/IAppUserStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Auth/Interfaces/IAppUserStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Auth/Interfaces/IAppUserStore.cs` | CREATE | target of git mv of `IAppUserStore.cs` (interface `IAppUserStore`) |
| `src/wa.application/UseCases/Auth/IAuthTokenStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Auth/Interfaces/IAuthTokenStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Auth/Interfaces/IAuthTokenStore.cs` | CREATE | target of git mv of `IAuthTokenStore.cs` (interface `IAuthTokenStore`) |
| `src/wa.application/UseCases/Auth/IEmailEventHandler.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Auth/Interfaces/IEmailEventHandler.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Auth/Interfaces/IEmailEventHandler.cs` | CREATE | target of git mv of `IEmailEventHandler.cs` (interface `IEmailEventHandler`) |
| `src/wa.application/UseCases/Auth/IOrgLogoStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Auth/Interfaces/IOrgLogoStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Auth/Interfaces/IOrgLogoStore.cs` | CREATE | target of git mv of `IOrgLogoStore.cs` (interface `IOrgLogoStore`) |
| `src/wa.application/UseCases/Auth/IOrgNameStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Auth/Interfaces/IOrgNameStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Auth/Interfaces/IOrgNameStore.cs` | CREATE | target of git mv of `IOrgNameStore.cs` (interface `IOrgNameStore`) |
| `src/wa.application/UseCases/Billing/EndBillingGrace.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Billing/EndBillingGraceHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Billing/EndBillingGraceHandler.cs` | CREATE | target of git mv of `EndBillingGrace.cs` (class `EndBillingGraceHandler`) |
| `src/wa.application/UseCases/Billing/SendBillingEmails.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Billing/SendBillingEmailsHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Billing/SendBillingEmailsHandler.cs` | CREATE | target of git mv of `SendBillingEmails.cs` (class `SendBillingEmailsHandler`) |
| `src/wa.application/UseCases/DataExport/BuildDataExports.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/DataExport/BuildDataExportsHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/DataExport/BuildDataExportsHandler.cs` | CREATE | target of git mv of `BuildDataExports.cs` (class `BuildDataExportsHandler`) |
| `src/wa.application/UseCases/EmailSettings/IEmailNotificationsStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/EmailSettings/Interfaces/IEmailNotificationsStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/EmailSettings/Interfaces/IEmailNotificationsStore.cs` | CREATE | target of git mv of `IEmailNotificationsStore.cs` (interface `IEmailNotificationsStore`) |
| `src/wa.application/UseCases/Gdpr/SweepErasedTelemetry.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Gdpr/SweepErasedTelemetryHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Gdpr/SweepErasedTelemetryHandler.cs` | CREATE | target of git mv of `SweepErasedTelemetry.cs` (class `SweepErasedTelemetryHandler`) |
| `src/wa.application/UseCases/Transfers/ActivateScheduledTransfers.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/ActivateScheduledTransfersHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/ActivateScheduledTransfersHandler.cs` | CREATE | target of git mv of `ActivateScheduledTransfers.cs` (class `ActivateScheduledTransfersHandler`) |
| `src/wa.application/UseCases/Transfers/DeleteExpiredTransfers.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/DeleteExpiredTransfersHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/DeleteExpiredTransfersHandler.cs` | CREATE | target of git mv of `DeleteExpiredTransfers.cs` (class `DeleteExpiredTransfersHandler`) |
| `src/wa.application/UseCases/Transfers/DeleteOrphanBlobs.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/DeleteOrphanBlobsHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/DeleteOrphanBlobsHandler.cs` | CREATE | target of git mv of `DeleteOrphanBlobs.cs` (class `DeleteOrphanBlobsHandler`) |
| `src/wa.application/UseCases/Transfers/Errors.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/TransferSizeExceededException.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/TransferSizeExceededException.cs` | CREATE | target of git mv of `Errors.cs` (class `TransferSizeExceededException`) |
| `src/wa.application/UseCases/Transfers/ExpireTransfers.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/ExpireTransfersHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/ExpireTransfersHandler.cs` | CREATE | target of git mv of `ExpireTransfers.cs` (class `ExpireTransfersHandler`) |
| `src/wa.application/UseCases/Transfers/ITransferSearchStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/Interfaces/ITransferSearchStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferSearchStore.cs` | CREATE | target of git mv of `ITransferSearchStore.cs` (interface `ITransferSearchStore`) |
| `src/wa.application/UseCases/Transfers/ITransferStore.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/Interfaces/ITransferStore.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferStore.cs` | CREATE | target of git mv of `ITransferStore.cs` (interface `ITransferStore`) |
| `src/wa.application/UseCases/Transfers/IZipFileSource.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/Interfaces/IZipFileSource.cs` (interface → Interfaces/); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/Interfaces/IZipFileSource.cs` | CREATE | target of git mv of `IZipFileSource.cs` (interface `IZipFileSource`) |
| `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs` | MODIFY | `git mv` → `src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs` (rename to match type name); content unchanged in Phase A |
| `src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs` | CREATE | target of git mv of `SendNotificationEmails.cs` (class `SendNotificationEmailsHandler`) |

#### Phase A — src/wa.domain

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Analytics/DownloadAnalytics.cs` | MODIFY | Split (8 types). keep `DownloadAnalytics` only; extract: record DailyCount, record BucketCount, record AnalyticsFile, record FileCountRow, record FileDownloadCount, record FileBreakdown, record TransferAnalytics |
| `src/wa.domain/Analytics/DailyCount.cs` | CREATE | record `DailyCount` from `DownloadAnalytics.cs` |
| `src/wa.domain/Analytics/BucketCount.cs` | CREATE | record `BucketCount` from `DownloadAnalytics.cs` |
| `src/wa.domain/Analytics/AnalyticsFile.cs` | CREATE | record `AnalyticsFile` from `DownloadAnalytics.cs` |
| `src/wa.domain/Analytics/FileCountRow.cs` | CREATE | record `FileCountRow` from `DownloadAnalytics.cs` |
| `src/wa.domain/Analytics/FileDownloadCount.cs` | CREATE | record `FileDownloadCount` from `DownloadAnalytics.cs` |
| `src/wa.domain/Analytics/FileBreakdown.cs` | CREATE | record `FileBreakdown` from `DownloadAnalytics.cs` |
| `src/wa.domain/Analytics/TransferAnalytics.cs` | CREATE | record `TransferAnalytics` from `DownloadAnalytics.cs` |
| `src/wa.domain/Correlation/W3CTraceParser.cs` | MODIFY | Split (2 types). keep `W3CTraceParser` only; extract: record TraceParent |
| `src/wa.domain/Correlation/TraceParent.cs` | CREATE | record `TraceParent` from `W3CTraceParser.cs` |
| `src/wa.domain/DataExport.cs` | MODIFY | Split (2 types). keep `DataExport` only; extract: class DataExportState |
| `src/wa.domain/DataExportState.cs` | CREATE | class `DataExportState` from `DataExport.cs` |
| `src/wa.domain/EventEnvelope.cs` | MODIFY | Split (3 types). keep `EventEnvelope` only; extract: enum EventType, class EventTypeConverter |
| `src/wa.domain/EventType.cs` | CREATE | enum `EventType` from `EventEnvelope.cs` |
| `src/wa.domain/EventTypeConverter.cs` | CREATE | class `EventTypeConverter` from `EventEnvelope.cs` |
| `src/wa.domain/PlanPrice.cs` | MODIFY | Split (3 types). keep `PlanPrice` only; extract: enum BillingInterval, class PlanPriceTier |
| `src/wa.domain/BillingInterval.cs` | CREATE | enum `BillingInterval` from `PlanPrice.cs` |
| `src/wa.domain/PlanPriceTier.cs` | CREATE | class `PlanPriceTier` from `PlanPrice.cs` |
| `src/wa.domain/Transfers/CursorCodec.cs` | MODIFY | Split (2 types). keep `CursorCodec` only; extract: class CursorFormatException |
| `src/wa.domain/Transfers/CursorFormatException.cs` | CREATE | class `CursorFormatException` from `CursorCodec.cs` |
| `src/wa.domain/Transfers/SearchTerm.cs` | MODIFY | Split (2 types). keep `SearchTerm` only; extract: class SearchTermTooLongException |
| `src/wa.domain/Transfers/SearchTermTooLongException.cs` | CREATE | class `SearchTermTooLongException` from `SearchTerm.cs` |

#### Phase A — src/wa.infrastructure

| File | Action | What to change |
|---|---|---|
| `src/wa.infrastructure/Blob/BlobSasMinter.cs` | MODIFY | Split (2 types). keep `BlobSasMinter` only; extract: class SasLogMask |
| `src/wa.infrastructure/Blob/SasLogMask.cs` | CREATE | class `SasLogMask` from `BlobSasMinter.cs` |
| `src/wa.infrastructure/Persistence/DataExportStore.cs` | MODIFY | Split (2 types). keep `DataExportStore` only; extract: class DataExportJobStore |
| `src/wa.infrastructure/Persistence/DataExportJobStore.cs` | CREATE | class `DataExportJobStore` from `DataExportStore.cs` |
| `src/wa.infrastructure/Persistence/LimitsStores.cs` | MODIFY | Split (2 types). **DELETE** (git rm) after extracting all types; extract: class PlansStore, class FlagsStore |
| `src/wa.infrastructure/Persistence/PlansStore.cs` | CREATE | class `PlansStore` from `LimitsStores.cs` |
| `src/wa.infrastructure/Persistence/FlagsStore.cs` | CREATE | class `FlagsStore` from `LimitsStores.cs` |
| `src/wa.infrastructure/Persistence/UnsubscribeService.cs` | MODIFY | Split (2 types). keep `UnsubscribeService` only; extract: record UnsubscribeResult |
| `src/wa.infrastructure/Persistence/UnsubscribeResult.cs` | CREATE | record `UnsubscribeResult` from `UnsubscribeService.cs` |

#### Phase A — tests/wa.api.integration

| File | Action | What to change |
|---|---|---|
| `tests/wa.api.integration/LoginEndpointTests.cs` | MODIFY | Split (2 types). keep `LoginEndpointTests` only; extract: class LoginEndpointTelemetryCapture |
| `tests/wa.api.integration/LoginEndpointTelemetryCapture.cs` | CREATE | class `LoginEndpointTelemetryCapture` from `LoginEndpointTests.cs` |
| `tests/wa.api.integration/ScheduledSendEndpointTests.cs` | MODIFY | Split (3 types). keep `ScheduledSendEndpointTests` only; extract: class ScheduledSendEndpointTestsBase, class ScheduledSendEndpointTests_GateOff |
| `tests/wa.api.integration/ScheduledSendEndpointTestsBase.cs` | CREATE | class `ScheduledSendEndpointTestsBase` from `ScheduledSendEndpointTests.cs` |
| `tests/wa.api.integration/ScheduledSendEndpointTests_GateOff.cs` | CREATE | class `ScheduledSendEndpointTests_GateOff` from `ScheduledSendEndpointTests.cs` |
| `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs` | MODIFY | Split (3 types). keep `TransferAnalyticsEndpointTests` only; extract: class TransferAnalyticsEndpointTestsBase, class TransferAnalyticsEndpointTests_GateOff |
| `tests/wa.api.integration/TransferAnalyticsEndpointTestsBase.cs` | CREATE | class `TransferAnalyticsEndpointTestsBase` from `TransferAnalyticsEndpointTests.cs` |
| `tests/wa.api.integration/TransferAnalyticsEndpointTests_GateOff.cs` | CREATE | class `TransferAnalyticsEndpointTests_GateOff` from `TransferAnalyticsEndpointTests.cs` |
| `tests/wa.api.integration/TransferSearchEndpointTests.cs` | MODIFY | Split (3 types). keep `TransferSearchEndpointTests` only; extract: class TransferSearchEndpointTestsBase, class TransferSearchEndpointGateOffTests |
| `tests/wa.api.integration/TransferSearchEndpointTestsBase.cs` | CREATE | class `TransferSearchEndpointTestsBase` from `TransferSearchEndpointTests.cs` |
| `tests/wa.api.integration/TransferSearchEndpointGateOffTests.cs` | CREATE | class `TransferSearchEndpointGateOffTests` from `TransferSearchEndpointTests.cs` |
| `tests/wa.api.integration/UnlockEndpointTests.cs` | MODIFY | Split (2 types). keep `UnlockEndpointTests` only; extract: class UnlockEndpointTelemetryCapture |
| `tests/wa.api.integration/UnlockEndpointTelemetryCapture.cs` | CREATE | class `UnlockEndpointTelemetryCapture` from `UnlockEndpointTests.cs` |
| `tests/wa.api.integration/UnsubscribeEndpointTests.cs` | MODIFY | Split (2 types). keep `UnsubscribeEndpointTests` only; extract: class UnsubscribeTelemetryCapture |
| `tests/wa.api.integration/UnsubscribeTelemetryCapture.cs` | CREATE | class `UnsubscribeTelemetryCapture` from `UnsubscribeEndpointTests.cs` |

#### Phase A — tests/wa.application.unit

| File | Action | What to change |
|---|---|---|
| `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` | MODIFY | Split (2 types). keep `WaTelemetryReporterTests` only; extract: class RecordingLogger |
| `tests/wa.application.unit/Telemetry/RecordingLogger.cs` | CREATE | class `RecordingLogger` from `WaTelemetryReporterTests.cs` |

#### Phase A — tests/wa.workers.unit

| File | Action | What to change |
|---|---|---|
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | Split (2 types). keep `WorkerServicesTests` only; extract: class WorkerServicesCollection |
| `tests/wa.workers.unit/WorkerServicesCollection.cs` | CREATE | class `WorkerServicesCollection` from `WorkerServicesTests.cs` |

#### Phase B — src/wa.api (12 files, 17 missing + 5 incomplete)

| File | Action | What to change |
|---|---|---|
| `src/wa.api/Middleware/BusinessException.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.api/Middleware/CorrelationMiddleware.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.api/Middleware/CorsAllowListMiddleware.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.api/Middleware/FeatureGateEndpointFilter.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.api/Middleware/GlobalErrorMiddleware.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.api/Middleware/IdempotencyMiddleware.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.api/Middleware/LogRoute.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.api/Middleware/PlanContextMiddleware.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.api/Middleware/ProblemFieldError.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.api/Middleware/ProblemWriter.cs` | DOCS | 1 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.api/Middleware/SessionMiddleware.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1570 |
| `src/wa.api/Program.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |

#### Phase B — src/wa.application (231 files, 316 missing + 143 incomplete)

| File | Action | What to change |
|---|---|---|
| `src/wa.application/Blob/BlobPaths.cs` | DOCS | 0 missing (CS1591) + 5 incomplete (no param/returns) |
| `src/wa.application/Events/EventDeduplicator.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/Limits/FeatureGates.cs` | DOCS | 6 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Limits/FeaturesProvider.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Limits/FlagValueRules.cs` | DOCS | 0 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.application/Limits/Interfaces/IFlagResolver.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Limits/Interfaces/IPlanContextAccessor.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Limits/LimitsProvider.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Limits/PlanContext.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/Limits/PlanContextAccessor.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/Ports/ComponentStatus.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Ports/FileEntry.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/Ports/IBlobContentWriter.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1573 |
| `src/wa.application/Ports/IBlobStore.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1573 |
| `src/wa.application/Ports/IEmailSender.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Ports/IExportSasMinter.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Ports/IIdempotencyStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Ports/IOrgLogoSasMinter.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Ports/ITemplateRenderer.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Ports/IdempotencyEntry.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/Ports/PlanUnknownException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/Telemetry/WaTelemetryReporter.cs` | DOCS | 1 missing (CS1591) + 8 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AdminDeleteTransferCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AdminDeleteTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AdminForceDeleteResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AdminForceDeleteUserCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AdminForceDeleteUserHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AdminRolePolicy.cs` | DOCS | 0 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/AuditWriter.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/ErasureListItem.cs` | DOCS | 8 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/ErasureReportPolicy.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetErasuresQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetErasuresQueryHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetErasuresResult.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetFlagsQueryHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetOverviewQueryHandler.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetRecentAuditQueryHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetSuppressionsQueryHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/GetUsersQueryHandler.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/Interfaces/IAdminUserStore.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1573 |
| `src/wa.application/UseCases/Admin/Interfaces/IErasureReportStore.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/Interfaces/IOverviewStore.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1573 |
| `src/wa.application/UseCases/Admin/Interfaces/ISearchTransfersStore.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1573 |
| `src/wa.application/UseCases/Admin/OverviewSnapshot.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/OverviewSnapshotCache.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/RemoveSuppressionCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/RemoveSuppressionCommandHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SearchTransfersQueryHandler.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SearchTransfersValidationException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetFlagCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetFlagCommandHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetFlagException.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetFlagResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetUserPlanCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetUserPlanCommandHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetUserPlanException.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Admin/SetUserPlanResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/DeleteAccountCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/DeleteAccountHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/DeleteAccountResult.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/ForgotPasswordCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/ForgotPasswordHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/ForgotPasswordValidationException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/GetOrgLogoHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/GetOrgLogoQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/GetProfileCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/GetProfileHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/GetProfileResult.cs` | DOCS | 9 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/Interfaces/IEmailEventHandler.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LinkExpiredException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LoginCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LoginHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LoginValidationException.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LoginWrongPasswordException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LogoContentSniffer.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/LogoImageType.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/MeProfileException.cs` | DOCS | 0 missing (CS1591) + 5 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/OrgLogoRejectedException.cs` | DOCS | 1 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/OrgLogoView.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/OrgNamePatch.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/OrgNamePatchJsonConverter.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RedeemValidationException.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RemoveOrgLogoCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RemoveOrgLogoHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkValidationException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/SessionTokenService.cs` | DOCS | 3 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/SignupCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/SignupHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/SignupValidationException.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/UpdateProfileCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/UpdateProfileHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/UpdateProfileRequest.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/UploadOrgLogoCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Auth/UploadOrgLogoHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/BillingEmailSchedule.cs` | DOCS | 2 missing (CS1591) + 5 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/BillingGrace.cs` | DOCS | 3 missing (CS1591) + 5 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/BillingSequenceRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/BillingUnavailableException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/CheckoutException.cs` | DOCS | 8 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/CheckoutRequest.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/CreateCheckoutSessionHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/EndBillingGraceHandler.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/GetPlanUsageHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/GetPortalSessionHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/GetPortalSessionQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/GraceCandidate.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/GraceDowngradeOutcome.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/Interfaces/IBillingSequenceStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/InvoicePaidOutcome.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/PaymentFailedOutcome.cs` | DOCS | 6 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/PaymentFailedResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/PlanUsageResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/PortalSessionResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/SendBillingEmailsHandler.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeApplyOutcome.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeApplyResult.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeCheckoutRequest.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeCheckoutSession.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripePortalSession.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeSubscriptionSnapshot.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeWebhookException.cs` | DOCS | 2 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeWebhookOutcome.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Billing/StripeWebhookSignature.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/BuildDataExportsHandler.cs` | DOCS | 1 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/DataExportArchive.cs` | DOCS | 2 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/DataExportInFlightException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/DataExportView.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/ExportFile.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/ExportMyDataCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/ExportMyDataHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/ExportProfile.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/ExportSnapshot.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/GetDataExportHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/GetDataExportQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/DataExport/PurgeableExport.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/EmailSettings/OwnerNotificationGate.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Features/GetFeatureGatesQueryHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Gdpr/Interfaces/IGdprSweepStore.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Gdpr/Interfaces/ITelemetryEraser.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Gdpr/SweepErasedTelemetryHandler.cs` | DOCS | 1 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Gdpr/TelemetryUnavailableException.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Health/HealthQuery.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Health/HealthQueryHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Health/HealthResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Sample/PingCommand.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Sample/PingCommandHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ActivateScheduledTransfersHandler.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ActiveTransfersExceededException.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/AnalyticsForbiddenException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/AnalyticsTransfer.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/BlobDeletionBatchRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/BlobSizeMismatchException.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/CreateDraftCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/CreateDraftFile.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/CreateDraftHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/CreateDraftResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DeleteExpiredTransfersHandler.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DeleteOrphanBlobsHandler.cs` | DOCS | 1 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DeleteTransferCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DeleteTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DownloadAllState.cs` | DOCS | 7 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DraftFile.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/DueScheduledRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ExpireTransfersHandler.cs` | DOCS | 1 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ExpiryBatchRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FileDownloadState.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FileItemRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FilesGoneException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FinalizeFile.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FinalizeTransferCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FinalizeTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/FinalizedFile.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetFileDownloadUrlHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetMyTransferFile.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetMyTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1570 |
| `src/wa.application/UseCases/Transfers/GetMyTransferQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetMyTransferResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns); also fix CS1570 |
| `src/wa.application/UseCases/Transfers/GetPublicTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetTransferAnalyticsHandler.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/GetTransferAnalyticsQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/Interfaces/IEmailStore.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/Interfaces/ISendStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/Interfaces/ISenderBrandingStore.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferSearchStore.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ListMyTransfersHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ListMyTransfersQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ListMyTransfersResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/MarkDeleteResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/PublicFileItem.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/PublicTransferState.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/RecipientAds.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ResendFileItem.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ResendForbiddenException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ResendTransferCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ResendTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ScheduleForbiddenException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/ScheduleOutOfWindowException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SearchMyTransfersHandler.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SearchMyTransfersQuery.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SendTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SenderBranding.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SenderBrandingRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SenderBrandingView.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SingleFileDownloadOutcome.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/SingleFileDownloadResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/StorageQuotaExceededException.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/TemplateLocaleResolver.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/TransferDeletionBatchRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/TransferSizeExceededException.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/TransferValidationException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/UnlockPasswordMissingException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/UnlockState.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/UnlockTokenService.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/UnlockTransferCommand.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/UnlockTransferHandler.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.application/UseCases/Transfers/UnsubscribeToken.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |

#### Phase B — src/wa.domain (51 files, 195 missing + 48 incomplete)

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Analytics/AnalyticsFile.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Analytics/BrowserFamily.cs` | DOCS | 6 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Analytics/BucketCount.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Analytics/DailyCount.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Analytics/DownloadAnalytics.cs` | DOCS | 0 missing (CS1591) + 7 incomplete (no param/returns) |
| `src/wa.domain/Analytics/FileCountRow.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Analytics/FileDownloadCount.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/AppUser.cs` | DOCS | 6 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/AuditLog.cs` | DOCS | 6 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/AuthToken.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/BillingInterval.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/BlobRef.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Correlation/CorrelationContext.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Correlation/TraceParent.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.domain/Correlation/W3CTraceParser.cs` | DOCS | 0 missing (CS1591) + 5 incomplete (no param/returns) |
| `src/wa.domain/DataExport.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/DataExportState.cs` | DOCS | 3 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/DomainEvent.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/DownloadEvent.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/EmailRecipient.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/EmailSuppression.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Erasure.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Errors/ProblemCodes.cs` | DOCS | 28 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.domain/EventType.cs` | DOCS | 13 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/EventTypeConverter.cs` | DOCS | 2 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/FeatureFlag.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/FileItem.cs` | DOCS | 7 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/IdempotencyKey.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/JobRun.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/LimitsRecord.cs` | DOCS | 0 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.domain/Plan.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/PlanDefinition.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/PlanFeatures.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/PlanPrice.cs` | DOCS | 3 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/PlanPriceTier.cs` | DOCS | 2 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Privacy/EmailMask.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/StripeEvent.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Subscription.cs` | DOCS | 8 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Telemetry/PiiTransforms.cs` | DOCS | 0 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.domain/Telemetry/TelemetryEvents.cs` | DOCS | 33 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Transfer.cs` | DOCS | 13 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/TransferStatus.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Transfers/CursorCodec.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.domain/Transfers/CursorFormatException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Transfers/ScheduleWindow.cs` | DOCS | 5 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Transfers/ScheduledSend.cs` | DOCS | 0 missing (CS1591) + 4 incomplete (no param/returns) |
| `src/wa.domain/Transfers/SearchCursorCodec.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Transfers/SearchTerm.cs` | DOCS | 2 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/Transfers/SearchTermTooLongException.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.domain/Transfers/SendTimeLimits.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.domain/ValueObjects/LinkId.cs` | DOCS | 0 missing (CS1591) + 3 incomplete (no param/returns) |

#### Phase B — src/wa.infrastructure (76 files, 161 missing + 43 incomplete)

| File | Action | What to change |
|---|---|---|
| `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` | DOCS | 3 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Blob/AzureBlobStore.cs` | DOCS | 6 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.infrastructure/Blob/BlobContainerBootstrap.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Blob/BlobSasMinter.cs` | DOCS | 1 missing (CS1591) + 6 incomplete (no param/returns); also fix CS1574 |
| `src/wa.infrastructure/Blob/DraftBlobSasMinter.cs` | DOCS | 0 missing (CS1591) + 4 incomplete (no param/returns) |
| `src/wa.infrastructure/Blob/SasLogMask.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix internal type + its members (not reported by CS1591 — document by hand) |
| `src/wa.infrastructure/Email/CommunicationHubEmailSender.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns); also fix CS1573 |
| `src/wa.infrastructure/Email/EmailSenderSelector.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Email/LogEmailSender.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Email/StubbleTemplateRenderer.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Events/InMemoryEventPublisher.cs` | DOCS | 0 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.infrastructure/Events/SbEventPublisher.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Events/SbHealthProbe.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AdminFlagStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AdminSuppressionStore.cs` | DOCS | 1 missing (CS1591) + 2 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AdminTransferStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AdminUserStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AppDbContextFactory.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AppUserStore.cs` | DOCS | 2 missing (CS1591) + 4 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AuditReadStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AuditStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/AuthTokenStore.cs` | DOCS | 1 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/BillingGraceStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/BillingSequenceStore.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/BlobDeletionStore.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/DataExportJobStore.cs` | DOCS | 9 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/DataExportStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/DesignTime/WaDbContextFactory.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EmailNotificationsStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EmailStore.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/AuditLogConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/AuthTokenConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/BlobRefConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/DataExportConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/DomainEventsConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/DownloadEventConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/EmailRecipientConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/EmailSuppressionConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/ErasureConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/FileItemConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/IdempotencyKeyConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/JobRunConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/StripeEventConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/SubscriptionConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/TransferConfiguration.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/ErasureReportStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/ExpiryStore.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/FlagsStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/GdprSweepStore.cs` | DOCS | 5 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/IdempotencyStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/OrgLogoStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/OrgNameStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/OverviewStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/PlansStore.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/PublicTransferStore.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/ScheduledSendStore.cs` | DOCS | 5 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/SearchTransfersStore.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/SendStore.cs` | DOCS | 5 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/SenderBrandingStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/StripeBillingStore.cs` | DOCS | 4 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` | DOCS | 3 missing (CS1591) + 4 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/TransferSearchStore.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/TransferStore.cs` | DOCS | 5 missing (CS1591) + 4 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/TransferTrackerExtensions.cs` | DOCS | 0 missing (CS1591) + 0 incomplete (no param/returns); also fix internal type + its members (not reported by CS1591 — document by hand) |
| `src/wa.infrastructure/Persistence/UnsubscribeResult.cs` | DOCS | 0 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/UnsubscribeService.cs` | DOCS | 1 missing (CS1591) + 1 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/UserPlanStore.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/WaDbContext.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/WaDbContextFactory.cs` | DOCS | 3 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/WaDbHealthProbe.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Persistence/ZipFileSource.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.infrastructure/Telemetry/ClosedSetTelemetryEraser.cs` | DOCS | 1 missing (CS1591) + 0 incomplete (no param/returns) |

#### Phase B — src/wa.workers (9 files, 20 missing + 4 incomplete)

| File | Action | What to change |
|---|---|---|
| `src/wa.workers/DataExportFunction.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/DeleteBlobsFunction.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/DeleteTransfersFunction.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/EmailFunction.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/ExpireFunction.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/GdprSweepFunction.cs` | DOCS | 2 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/JobAuth.cs` | DOCS | 0 missing (CS1591) + 3 incomplete (no param/returns) |
| `src/wa.workers/WorkerServices.cs` | DOCS | 6 missing (CS1591) + 0 incomplete (no param/returns) |
| `src/wa.workers/ZipFunction.cs` | DOCS | 2 missing (CS1591) + 1 incomplete (no param/returns) |

#### Guard allow-list edits (existing code → target)

Every target path below was derived by mapping each regex hit to the type that owns that line after the split. Keep the trailing `//` reason comments.

`tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs:16-32` today:
```csharp
    private static readonly string[] FeaturesProviderAllowList =
    [
        "src/wa.application/Limits/FeaturesProvider.cs", // definition
        "src/wa.application/Limits/PlanContext.cs", // the single consumer of the viewer's plan
        "src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs", // US-021-01 ...
        "src/wa.application/UseCases/Transfers/SenderBranding.cs", // US-021-02 ...
        "src/wa.application/UseCases/Transfers/RecipientAds.cs", // US-024-02 ...
        "src/wa.application/UseCases/Transfers/SendNotificationEmails.cs", // US-021-02 ...
        "src/wa.workers/WorkerServices.cs", // US-021-02 ...
        "src/wa.api/Program.cs" // DI wiring
    ];

    private static readonly string[] PlanCodeComparisonAllowList =
    [
        "src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs", // checkout target / already-paid identity
        "src/wa.infrastructure/Persistence/BillingGraceStore.cs" // downgrade-to-free row lookup
    ];
```
Target:
- FeaturesProviderAllowList:
  - keep `FeaturesProvider.cs`;
  - **add** `src/wa.application/Limits/Interfaces/IFeaturesProvider.cs` (definition, interface);
  - `Limits/PlanContext.cs` → `src/wa.application/Limits/PlanContextAccessor.cs`;
  - `GetPublicTransferQuery.cs` → `src/wa.application/UseCases/Transfers/GetPublicTransferHandler.cs`;
  - `SendNotificationEmails.cs` → `src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs` (rename);
  - `SenderBranding.cs`, `RecipientAds.cs`, `WorkerServices.cs` and `Program.cs` are unchanged.
- PlanCodeComparisonAllowList: `CreateCheckoutSessionCommand.cs` → `src/wa.application/UseCases/Billing/CreateCheckoutSessionHandler.cs`.

`tests/wa.domain.unit/Plans/PlanIdConsumerGuardTests.cs:12-19` today lists `SetUserPlanCommand.cs`, `Auth/Login.cs`, `Auth/MeProfile.cs`, `Auth/RedeemMagicLink.cs` and `Billing/CreateCheckoutSessionCommand.cs`. Target:
- `src/wa.application/UseCases/Admin/SetUserPlanCommandHandler.cs`
- `src/wa.application/UseCases/Auth/LoginHandler.cs`
- `src/wa.application/UseCases/Auth/GetProfileHandler.cs` + `src/wa.application/UseCases/Auth/UpdateProfileHandler.cs` (MeProfile.cs had hits in both handlers)
- `src/wa.application/UseCases/Auth/RedeemMagicLinkHandler.cs`
- `src/wa.application/UseCases/Billing/CreateCheckoutSessionHandler.cs`

`tests/wa.domain.unit/Plans/LimitsJsonConsumerGuardTests.cs:13`: `src/wa.application/UseCases/Admin/GetFlagsQuery.cs` → `src/wa.application/UseCases/Admin/GetFlagsQueryHandler.cs`.

No change is needed in FeaturesJsonConsumerGuardTests, PriceJsonConsumerGuardTests, PlanCodeLiteralGuardTests or FeatureGateKeyLiteralGuardTests. Their allow-listed files (FeaturesProvider.cs, PlanDefinition.cs, PlanPrice.cs, Plan.cs, FeatureGates.cs, the EntityConfigurations) keep their matching type. Note that `src/wa.domain/PlanPrice.cs` keeps PlanPrice; the `.PriceJson` hit at line 38 belongs to it.

#### `src/Directory.Build.props` content

Phase B step 0:
```xml
<Project>
  <!-- CHORE-code-style: XML docs on src/ C# projects only (tests/ do not inherit this file; wa.web.esproj is excluded by the condition). -->
  <PropertyGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```
Phase C adds the following inside the same PropertyGroup:
```xml
    <WarningsAsErrors>$(WarningsAsErrors);CS1570;CS1572;CS1573;CS1574;CS1587;CS1591</WarningsAsErrors>
```

#### `src/.editorconfig` content (Phase C)
```ini
# CHORE-code-style §2.3: interfaces live in Interfaces/ folders but keep their area namespace (no .Interfaces suffix).
[**/Interfaces/*.cs]
dotnet_style_namespace_match_folder = false
```

#### Non-obvious context for the Coder
- `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` splits into the command record (stays), `CheckoutRequest.cs`, `CheckoutException.cs` and `CreateCheckoutSessionHandler.cs`. `CheckoutException.cs` keeps its nested public enum `FailureCode`. References like `wa.application.UseCases.Billing.CheckoutException.FailureCode.NotFound` (`src/wa.api/Program.cs:1479`) stay valid.
- `src/wa.application/UseCases/Auth/MeProfile.cs` (313 lines, 12 types) is the largest split. `OrgNamePatch` is a `record struct` with a `[JsonConverter]` link to `OrgNamePatchJsonConverter`. Splitting them into two files in the same namespace keeps the attribute target valid. Do not rename either.
- `src/wa.domain/EventEnvelope.cs`: EventType (enum), EventEnvelope and EventTypeConverter split into 3 files. The converter is the wire-name mapping of TA-5.3. Move it byte-for-byte; no JSON name changes.
- `src/wa.application/Ports/Events.cs` (deleted) becomes `DomainEventRecord.cs` + `IEventPublisher.cs` in Ports. `src/wa.application/Ports/Health.cs` (deleted) becomes `ComponentStatus.cs`, `IDbHealthProbe.cs` and `ISbHealthProbe.cs`.
- `src/wa.application/UseCases/Transfers/SendTransfer.cs` (deleted) contains the interface `ISendStore`, which goes to `UseCases/Transfers/Interfaces/ISendStore.cs`. `src/wa.infrastructure/Persistence/SendStore.cs` implements it; that file needs no edit (same namespace).
- `src/wa.application/UseCases/Auth/Signup.cs` (deleted) contains `ISessionTokenMinter` (→ `Auth/Interfaces/`) and `SessionTokenService`. `src/wa.application/UseCases/Transfers/UnlockToken.cs` (deleted) contains `IUnlockTokenMinter` (→ `Transfers/Interfaces/`) and `UnlockTokenService`.
- `tests/wa.api.integration/*EndpointTestsBase` classes are abstract `IAsyncLifetime` bases parameterised by the derived classes (gate on/off). Moving the base into its own file changes nothing for xUnit discovery.
- The codegraph index refreshes on its own (file watcher). The Coder does not touch `.codegraph/`.

## 5. Open Questions / Risks

1. **Merge-conflict blast radius (highest risk).** About 100 files are deleted or split and about 300 created in wa.application alone. Any Hermes branch open in parallel that edits e.g. `UseCases/Auth/MeProfile.cs` or `UseCases/Transfers/SendTransfer.cs` will conflict hard: git cannot follow a split. *Mitigation:* land Phase A while no other `feature/*` branch touches src/wa.application, then merge develop into those branches and re-apply their hunks to the new files. The orchestrator should hold the board (nothing in Ready that touches C#) until Phase A is merged. Phases B and C are lower risk (additive comment lines).
2. **Guard tests keyed on file paths.** These are the 3 allow-list files in §4. A missed path shows up as a red tests/wa.domain.unit with the exact new path in the message, so the failure is loud and the fix is mechanical. The guards ignore `//`-prefixed lines, so XML docs cannot trip them unless someone writes `/** */`, which §2.4 forbids.
3. **MediatR / EF / DI discovery.** All of it is assembly-scan based (`src/wa.api/Program.cs:35`, `src/wa.infrastructure/Persistence/WaDbContext.cs:29`), and namespaces do not change, so moving files cannot drop a handler or configuration. There are no `InternalsVisibleTo`, `file`-scoped types or source generators that a move could break. `public partial class Program` stays in Program.cs.
4. **docs/ go stale on file paths.** `docs/REPO-FACTS.md`, the TA doc and older `docs/plans/*` cite some deleted paths (e.g. `Ports/BlobStore.cs`, `UseCases/Auth/Login.cs`); 7 distinct src paths in REPO-FACTS/TA/AGENT docs. That is docs-only work. The orchestrator's close-out should refresh REPO-FACTS §-paths and add a CONVENTIONS line to `docs/AGENT.md` §3 or `AGENTS.md` ("one type per file; interfaces in `Ports/` or `<Area>/Interfaces/`; XML docs required in src, enforced by `src/Directory.Build.props`") so Hermes follows the rule. The Coder must not edit docs/.
5. **Phase C changes the contract for every future task.** From Phase C on, any public member added without `///` fails the build (CS1591 as error), for Hermes too. This is the user's stated intent. Orchestrator: make sure the Hermes instructions mention it (see 4). It is not an AGENT.md §7 escalation item: no package, no env var, no security setting, and no TBD-you decision is touched.
6. **Heuristic "incomplete" counts.** The roughly 243 summary-without-`<param>`/`<returns>` members come from a regex scan, not the compiler, so expect ±10 %. The compiler cannot enforce full `<param>` coverage when a member has no param tags at all. Real enforcement would need StyleCop.Analyzers (SA1611/SA1615), which is a new NuGet package: ADR + DECISION. Not proposed here; the user can ask for it as a follow-up.
7. **Integration suite needs Docker.** `docker version` returned nothing during planning, so the daemon is probably not running. The Coder must start Docker Desktop for the Phase C gate, or flag the run as pending for the Reviewer.
8. **Size.** Phase B is about 950 documentation sites, the bulk of the work. The B1–B4 commit split keeps each review manageable. The Reviewer should sample per project rather than read every line.
