# CHORE-code-style-2 — CI on develop, zero warnings, editorconfig, TimeProvider, LoggerMessage, analyzers, primary ctors, Program.cs split

**Type:** solution-wide, non-feature refactor (no T-xxx id). **Branch:** `chore/code-style-2` (from origin/develop @ 85aa611). **Plan date:** 2026-11-25.
**Follows:** `docs/plans/CHORE-code-style-plan.md` (one type per file, Interfaces/ folders, XML docs as errors — docs/AGENT.md §3 rule 10). Everything here must keep rule 10 green.
**Escalations:** two DECISION blocks in §5 (DECISION-1 login null hash, DECISION-2 CORS Vary header). Each one blocks only its own site. The rest of the plan does not assume an answer.

## 1. Goal

Make the C# solution warning-free and keep it so: `TreatWarningsAsErrors`, enforced style, built-in analyzers, `TimeProvider`, source-generated logging, primary constructors and a split `Program.cs`, with CI running on PRs into `develop`. No behaviour change apart from the two DECISION sites.
**Exit check:** `dotnet build src/wa.slnx` has 0 C# warnings (npm-audit lines from `src/wa.web/wa.web.esproj` excepted) with TWAE on. The 3 unit suites pass. `node scripts/check-telemetry-pii.mjs` and its `selftest` pass. `dotnet format src/wa.slnx` leaves `git status` clean. All 9 CI jobs are green on the PR `chore/code-style-2` → `develop`, integration included.

### Measured baseline (2026-11-25, 85aa611, local SDK 11.0.100 — CI uses 10.0.x)
- **Compiler warnings: 66 unique** (`dotnet build src/wa.slnx -t:Rebuild`).
  - CS0618 ×49: `new MsSqlBuilder()` in 49 files of tests/wa.api.integration.
  - CS8524 ×4: `src/wa.api/Program.cs` at lines 422, 479, 509 and 533.
  - CS8604 ×5: `src/wa.application/UseCases/Auth/LoginHandler.cs:58` plus 4 in tests.
  - Tests also hold CS8609, CS8619 ×2, CS8620, CS8631 and CS8764 ×3.
  - Plus 25 npm-audit warnings from the esproj SDK. They are not C# and stay out of TWAE scope.
- **Analyzers, measured with `-p:AnalysisLevel=10.0-recommended`.**
  - src: 196 CA hits. CA1848 ×88 and CA1873 ×34 (logging), CA1725 ×23, CA1861 ×15 (all in Migrations), CA2016 ×5, CA1305 ×5, CA1859 ×4, CA1850 ×3, CA1727 ×7 + CA2254 ×1 (all in WaTelemetryReporter), CA1822 ×2, CA2263 ×2, CA1835 ×2, CA1806, CA1716, CA1711.
  - tests: ~1,400 hits, CA1707 ×1,225 among them (xUnit `Method_State_Result` names).
  - `latest-recommended` on SDK 11 gave only 3 hits, so "latest" is not stable across SDKs. That is the reason to pin (§2.5).
- **Clock reads:** 74 textual `DateTime.UtcNow` / `DateTimeOffset.UtcNow` matches in 42 src files: 61 + 13, of which 3 are inside comments. 0 in `wa.domain`. 7 types already take a `Func<DateTimeOffset>` clock seam.
- **ILogger calls:** 88 `.Log<Level>(` calls in 36 files. 8 of them are in `src/wa.application/Telemetry/WaTelemetryReporter.cs`, which leaves **80 calls in 35 files**.
- **Primary-ctor candidates:** IDE0290 flags 120 constructors.
  - 77 are pure `_x = x;` assignment ctors: 74 in src, 3 nested fakes in tests. Only these are in scope.
  - 35 have logic: `?? throw` guards, `configuration[...]` reads or `new` expressions.
  - 8 call `: base(...)` (exceptions and WaDbContext).
- **dotnet format:** CI job `lint-dotnet` already runs dotnet format on the slnx with the verify-no-changes flag. On Linux it passed on 2026-11-20, but it never ran on develop PRs. Locally with an LF `.editorconfig` there are 13 real drift sites: TransferSearchStore ×9, PipelineTests ×2 and 2 missing final newlines. The ENDOFLINE hits are working-tree-only (4 files are CRLF in the working tree but LF in the index).
- **Program.cs:** 1,532 lines, 47 `app.Map*` calls (`/`, `/health`, the Development-only chaos route and 44 `/api/v1` + `/unsubscribe` routes). The DI block is at lines 35-180. One local function (`RequireAdminAsync`, `src/wa.api/Program.cs:195`) and 7 static local helpers. Three captures need care: `builder.Configuration` read per request at `src/wa.api/Program.cs:615` and `src/wa.api/Program.cs:1436`, the admin and operator sets at `src/wa.api/Program.cs:1078`, and `app.Environment` at `src/wa.api/Program.cs:237`.
- **Style (measured, the dominant style is kept):**
  - 649/651 files use file-scoped namespaces. 0 usings inside a namespace. 0 tabs.
  - Allman braces everywhere.
  - Private fields are `_camelCase` (471 vs 1 exception). Private static readonly fields and consts are PascalCase.
  - `var` is dominant. Using-order is mixed (360 sorted, 158 not), so it is not enforced.

## 2. Design & Decisions

### 2.1 Phase order (chosen to minimise rework)
| Phase | Commit | Why here |
|---|---|---|
| P1 | CI on develop + `.editorconfig` + EnforceCodeStyleInBuild | CI first, so every later commit on the PR is checked on Linux/SDK 10. The LF editorconfig must exist before any `dotnet format` run on Windows, otherwise it would write CRLF. |
| P2 | zero compiler warnings + TWAE (src + tests) | Locks the zero baseline at once. From here every phase must end with a clean build. |
| P3 | TimeProvider | Adds ctor params. Must run before P6 so the new params become primary-ctor params as well. |
| P4 | LoggerMessage + PII guard R7 | Must run before P5. Otherwise CA1848/CA1873 (122 hits) would be build errors under TWAE the moment the analyzers are switched on. |
| P5 | AnalysisLevel + CA fixes / config | Leaves only genuine CA hits (§2.5). |
| P6 | primary constructors | Ctor shapes are final after P3. The convention keeps the `_x` fields, so the P4 `LogX(_logger, …)` calls stay valid. |
| P7 | Program.cs split | Last code move. Every earlier phase cites pre-split `Program.cs` line numbers. Splitting earlier would make them all stale. |
| P8 | IDE0005 sweep + final gate | Removing usings shifts lines in 142 files. Doing it last keeps the line refs of P2-P7 valid until they are used. |

Each phase is one commit, `refactor: CHORE-code-style-2 P<n> — <title>`, and ends green on the gate in §3.0. **Line numbers in this plan are baseline (85aa611).** Find each site by the quoted code: earlier phases shift lines.

### 2.2 CI on develop (TA-12.1, TA-14.2)
- The on.pull_request.branches list of `.github/workflows/ci.yml` becomes `[main, develop]`. Nothing else changes in the trigger.
- No push trigger. TA-12.1 defines the PR pipeline, and TA-12.3 keeps `main` as the deploy branch.
- All 9 jobs are safe on develop PRs:
  - No secrets (header comment: "No secrets at M0").
  - `docker` builds without pushing.
  - `integration` uses runner Docker (EC-051-2). It takes ~40 min (run 44765068166). That matches TA-14.2 "PR: lint + unit + integration must pass".
- Update the stale `lint-dotnet` comment ("structural no-op until an .editorconfig lands") to say that it now enforces the repo `.editorconfig`.
- Open question (not done): a `concurrency` group to cancel superseded runs.

### 2.3 Zero warnings, then TWAE
- **CS0618:** Testcontainers 4.15.0 (`tests/wa.api.integration/wa.api.integration.csproj`) obsoletes the parameterless builder. The repo already uses the image ctor for Azurite (`new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")`). So the fix is `new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")` with the `.WithImage(...)` call removed. 44 files use one-line sites. 5 use a builder chain over several lines (DeleteTransfersJobIntegrationTests, OutboxRetryTests, ExpiryJobIntegrationTests, SeedDataIntegrationTest, DeleteTransferPurgesAnalyticsTests). Image and options stay the same.
- **CS8524** (4 switch expressions over `result.State` in Program.cs): add an arm `_ => throw new SwitchExpressionException(result.State)` (`using System.Runtime.CompilerServices;`). This is the exact exception the compiler-generated default throws today, so behaviour is byte-identical. Do not use `UnreachableException`.
- **CS8604 at LoginHandler.cs:58** is a **real defect**: a magic-link-only account has `PasswordHash == null` (`src/wa.domain/AppUser.cs:15`), so PasswordHasher throws and the response is 500. → **DECISION-1**.
- **Test nullable sites.** Use per-site fixes. Where `!` is used it follows the idiom already used in the same file. No `#pragma`.
  - DraftEndpointTests 91/114: `GetString()!` (line 113 already does this).
  - RedeemMagicLinkTests 85: `_tokens.Raw!` (line 102).
  - SignupEndpointTests 80: `row!.PasswordHash!` (after `Assert.NotNull(row)`).
  - PublicTransferEndpointTests 274: `GetString()!` in the Select, so the array is `string[]`.
  - AdminDelete / DeleteTransfer fakes: `Task.FromResult<MarkDeleteResult?>(...)` (the interface returns `Task<MarkDeleteResult?>`).
  - SendNotificationEmailsHandlerTests 38: `new Dictionary<string, string?>`.
  - ZipFunctionCorrelationTests stubs: match the base nullability. Details in the P2 steps.
- **TWAE.** Add `TreatWarningsAsErrors=true` to `src/Directory.Build.props` (already conditioned to `.csproj`, so the esproj and its npm-audit warnings are untouched). Add the same to a new `tests/Directory.Build.props`. Tests are recommended in because they are warning-free after P2 and are compiled by every gate. Tests do not inherit `src/` props, which is why they get their own file.
- `WarningsNotAsErrors=NU1901;NU1902;NU1903;NU1904`. A newly published package CVE must not break unrelated PRs. These stay visible warnings, exactly as today.

### 2.4 `.editorconfig` (dominant style, no rewrite) + enforcement
Two files, split by where they must apply:
- **Repo-root `.editorconfig` (CREATE, `root = true`):** formatting and naming for src and tests.
  - `end_of_line = lf`. This matches `.gitattributes` `* text=auto eol=lf` (T-051-01). Without it, `dotnet format` on Windows rewrites wrapped lines to CRLF; that was measured.
  - `insert_final_newline`, plus `charset = utf-8` and `trim_trailing_whitespace` for `*.cs`.
  - Allman `csharp_new_line_*`.
  - Set to `warning` (each is 0-3 hits today): `file_scoped` namespaces (IDE0161), `outside_namespace` usings (IDE0065), modifier order (IDE0036), `for_non_interface_members` accessibility (IDE0040), readonly fields (IDE0044), formatting (IDE0055) and naming (IDE1006).
  - Naming rules: interfaces `I`+Pascal; consts Pascal; private static readonly Pascal; other private fields `_camelCase`.
  - Preferences that match the tree but are **not** enforced (suggestion/silent): `var`, braces when multiline, and `dotnet_sort_system_directives_first = false`. Enforcing them would rewrite thousands of lines: IDE0008 ×5,070, IDE0011 ×666, IDE0058 ×1,088.
- **`src/.editorconfig` (MODIFY, not root).** It holds everything that must also apply inside the Docker builds. Those copy only `src/` into the build context (`src/wa.api/Dockerfile`: `COPY src ./`; `.dockerignore` drops tests/). A root file is invisible there. With TWAE and AnalysisLevel on, any CA config kept only at root would fail `docker build`. So the CA configuration (P5), the WaTelemetryReporter section, the Migrations `generated_code = true` / `charset = unset` section (EF writes a BOM and the generated code is not ours to lint) and IDE0005 (P8) all live here. The existing IDE0130 `[**/Interfaces/*.cs]` section stays as it is.
- **IDE0005 is src-only.** It needs `GenerateDocumentationFile`, which only src has. In tests the compiler would raise a warning about the missing documentation file instead, and TWAE would turn that into an error.
- `EnforceCodeStyleInBuild=true` in both props files (P1). The CI `lint-dotnet` step stays as it is.
- Validated in a scratch copy of the tree with exactly the §4.2 files: whitespace drift 13 sites; IDE warnings src 3 (IDE0040 ×2, IDE0055) + tests 6 (IDE0044 ×3, IDE0055 ×2, IDE1006 ×1); IDE0005 158 sites in 142 src files.

### 2.5 Built-in analyzers (no package; TA-17 unaffected)
- `<AnalysisLevel>10.0-recommended</AnalysisLevel>`, **pinned, not `latest-recommended`**:
  - Local SDK 11 and CI SDK 10 resolve "latest" differently (3 hits versus 196).
  - `global.json` uses `rollForward: latestMajor`.
  - 10.0 matches net10.0 and the `sdk:10.0` Docker images.
  - Put it in `src/Directory.Build.props` only. Tests get no AnalysisLevel: CA1707 ×1,225 fights xUnit naming, and CA1001 ×50 conflicts with IAsyncLifetime fixtures, so analyzing tests would be noise.
- **Fixed** (genuine, behaviour-identical):

  | Rule | Sites | Fix |
  |---|---|---|
  | CA1848 / CA1873 | 122 | P4 |
  | CA1835 | IdempotencyMiddleware 92, 98 | Memory overload `WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), context.RequestAborted)` |
  | CA1850 | 3 | `SHA256.HashData(..)`; same bytes |
  | CA1859 | 4, private members | concrete return/param type |
  | CA2263 | 2 | `Enum.GetValues<EventType>()` (the loop casts already exist); `Conventions.Remove<ForeignKeyIndexConvention>()` |
  | CA1822 | ZipFunction.VerifySignature (private) | `static` |
  | CA1305 | the 4 `int.ToString()` calls | `ToString(CultureInfo.InvariantCulture)`. Repo convention (`src/wa.api/Middleware/CorsAllowListMiddleware.cs:46`); same output for these non-negative counts |
  | CA1305 | Serilog `WriteTo.Console()` (`src/wa.api/Program.cs:29`) | `formatProvider: CultureInfo.CurrentCulture`. Identical, because nothing in src changes thread culture (no RequestLocalization) |
  | CA2016 | 5 | Pass `CancellationToken.None` **explicitly**: 3 `request.Body` copies in Program.cs (lines 275, 320, 468), the SB admin call (621) and the fire-and-forget `Task.Run` (GetDownloadAllUrlHandler 110). This is the analyzer's own "intentional" option. Forwarding the token would change cancellation behaviour; that is an Open Question. |
  | CA1806 | CorsAllowListMiddleware 40 | real defect → **DECISION-2** |
- **Configured off in `src/.editorconfig`** (one-line reason each):
  - CA1725 ×23: `ct` / `b` parameter abbreviations are the repo convention; there are no named-argument callers.
  - CA1716: VB keyword clash only.
  - CA1711: `IEmailEventHandler` is a handler contract by design.
  - CA2007: not raised at 10.0-recommended. Listed as off because ASP.NET Core and Functions have no SynchronizationContext.
- **The WaTelemetryReporter section** turns off CA1848, CA1873, CA2254, CA1727 and CA1822 for that one file. It is the single telemetry exit (AC-056-4):
  - Its `LogEvent(eventName)` template *is* the event name, a dynamic template by design (`src/wa.application/Telemetry/WaTelemetryReporter.cs:39`).
  - The lowercase placeholders are TA-10.2 property names that App Insights queries by, and T-056-05 pins them.
  - `FileNameSlot` must stay an instance method: the PII guard rule R4 keys on `<recv>.FileNameSlot(`.
- `generated_code = true` on Migrations silences CA1861 ×15 there.

### 2.6 TimeProvider (BCL, no package)
- **Registration:** `TimeProvider.System` registered as a singleton in both composition roots: `builder.Services.AddSingleton(TimeProvider.System)` in the API and `services.AddSingleton(TimeProvider.System)` at the top of `AddWorkerServices` (`src/wa.workers/WorkerServices.cs:40`). The inferred service type is `TimeProvider`.
- **Rewrites:**
  - `DateTime.UtcNow` becomes `_timeProvider.GetUtcNow().UtcDateTime`: `Kind = Utc`, the same `DateTime` type, so EF columns and Kind semantics are unchanged.
  - `DateTimeOffset.UtcNow` becomes `_timeProvider.GetUtcNow()` (offset zero).
  - Keep the **number and order of clock reads**. Two reads stay two reads (e.g. RedeemMagicLinkHandler 61 and 65). Never merge them.
- **Injection:** constructor param `TimeProvider timeProvider` with field `private readonly TimeProvider _timeProvider`.
  - Place it after the existing required params and before any optional `= null` params (AdminForceDeleteUserHandler, DeleteAccountHandler).
  - Follow the class's own assignment style. A class whose other params use `?? throw` guards (e.g. SessionMiddleware) guards it too.
  - MediatR handlers and middlewares resolve it through DI. Classes built by hand in a composition root (`new X(...)` lambdas in Program.cs / WorkerServices) get `sp.GetRequiredService<TimeProvider>()`. The compiler lists every site.
- **Existing `Func<DateTimeOffset>` seams stay:** PlansCache, FlagsCache, OverviewSnapshotCache, EventDeduplicator, SessionTokenService, UnlockTokenService, InMemoryEventPublisher, BlobSasMinter. They are already testable, and changing them would churn many tests. The composition roots pass `sp.GetRequiredService<TimeProvider>().GetUtcNow` (method group) instead of `() => DateTimeOffset.UtcNow`. The two defaults (`BlobSasMinter` `now ?? …`, `InMemoryEventPublisher()` ctor) become `TimeProvider.System.GetUtcNow`. Update the comment at Program.cs line 153.
- **Stripe webhook** (Program.cs line 1438): add a `TimeProvider timeProvider` lambda parameter. Minimal APIs bind a registered service from DI, and wa.api has no OpenAPI, so the endpoint metadata does not change.
- **Domain stays DI-free:** it has 0 clock reads. Its helpers already take `now` parameters. No change.
- **Tests:**
  - Hand-written `tests/wa.application.unit/ManualTimeProvider.cs` (`sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider` with settable `UtcNow`, `Advance(TimeSpan)` and `GetUtcNow()`). **No Microsoft.Extensions.TimeProvider.Testing**, so no new package and no DECISION.
  - Every changed test construction passes `TimeProvider.System`, which is behaviour-identical.
  - Two proof assertions use the fake. GetFileDownloadUrlHandlerTests asserts `ExpiresAt == fixedNow.AddMinutes(30)` exactly. AdminDeleteTransferCommandTests asserts the store received `fixedNow.UtcDateTime`.

### 2.7 Source-generated logging (TA-10.5, TA-9.4)
- **Convention:** `[LoggerMessage(Level = LogLevel.X, Message = "<exact current template>")] private static partial void Log<What>(ILogger logger, <params>);`. It is declared at the bottom of the owning type, which becomes `partial` (`public sealed partial class`).
  - **Static with an explicit logger:** it does not depend on how the logger is stored (field, primary-ctor param, nullable `_logger?` in AdminForceDeleteUserHandler / DeleteAccountHandler, the ctor parameter `logger` in `src/wa.infrastructure/Blob/AzureBlobStore.cs:33`).
  - **Private:** it adds no public surface, so CS1591 does not apply. The generated half carries `<auto-generated>`. Rule 10 needs no extra docs.
  - **Same type:** it stays one type per file and needs no new files.
  - Named `Message =` / `Level =` arguments only, never positional. The PII guard parses them (§2.8).
- **Preserve exactly:** the template text character for character, the level, the placeholder names (the structured property names are the placeholder spelling), the exception argument (an `Exception` param, which the generator treats as the exception), the argument expressions and their order.
  - Parameter names equal the placeholders, case-insensitively (`{C}` → `c`). SYSLIB1014/1015 then catch any mismatch, and TWAE makes that fatal.
  - Parameter types are the call-site argument types.
  - A template used twice in one type (StripeCheckoutGateway "transport failure" / "timed out") becomes one method called twice.
- **Call sites:**
  - `_logger.LogWarning("…", a, b)` → `LogX(_logger, a, b)`.
  - `_logger?.LogWarning(…)` → `if (_logger is not null) LogX(_logger, …)`.
  - If CA1873 still flags a call-site argument that is a method call (e.g. `LogRoute.Of(context)`), wrap it in `if (_logger.IsEnabled(LogLevel.X))`. The generated method makes the same check, so behaviour is identical.
- **EventId:** leave unset. The generator assigns an EventId and EventName (the method name), so Serilog gains an additive `EventId` property. Templates, levels and properties are otherwise unchanged. This is accepted as an inherent part of the requested conversion and is documented in REPO-FACTS close-out. If SYSLIB1006 fires, assign explicit ids 1..n per type.
- **Excluded:** `WaTelemetryReporter`, for the reasons in §2.5 and because its templates are the TA-10.2 wire contract. Its 8 calls stay as they are.
- Formatting stays identical for the unit-test loggers. `CapturingLogger` / `RecordingLogger` read `IEnumerable<KeyValuePair<string, object?>>` and `state.ToString()`, and the generated state struct implements both with the same `{OriginalFormat}` key.

### 2.8 PII guard — R7 must keep covering logging (`scripts/check-telemetry-pii.mjs`)
- Today R7 (`scanLog`, `scripts/check-telemetry-pii.mjs:338`) finds calls by LOG_CALL_RE (`.LogX(`). After P4, 80 calls stop matching. Without a script change, PII coverage silently drops to the 8 reporter calls.
- Add `scanLoggerMessage(file, text)`, a pure per-file function next to `scanLog`. It reports as **R7** (same rule id, so the summary table is unchanged), operates on the `mask()` output, and is called from `scanTree` (`scripts/check-telemetry-pii.mjs:499`) for every `.cs` file:
  1. **Attribute.** For each `[LoggerMessage(`, extract the args with `extractCall`.
     - No `Message =` named argument → R7 "unreadable [LoggerMessage] (use named Message = …)".
     - Otherwise apply the R7 template checks to the Message literal (from `nc`): email literal, and placeholders through `isLogPiiIdentifier`.
  2. **Declaration.** The next declaration after the attribute must match `private static partial void (\w+)(`. Anything else → R7 "[LoggerMessage] method must be private static partial (call sites outside the file escape the scan)".
     - For each parameter whose type is not `ILogger…` or `…Exception`: R7 when its name passes `isLogPiiIdentifier`.
     - Record the method name.
  3. **Call sites.** For each recorded name, find `(?<![\w.])Name\s*\(` that is not preceded by `void `. Drop the first top-level argument (the logger, via `splitTopLevel`). Run the existing R7 argument checks on the rest: email literal; `stripSanctioned` then PII identifiers; the `.Name` member; `.Path/.PathBase/.QueryString/.Message` members. Factor those checks out of `scanLog` into a shared `logArgReasons(argText, argCode)` so both paths use one implementation.
  4. **Counts.** `counts.loggerMessages` / `counts.loggerMessageCalls`. Print them in the scan summary line. `runScan` fails as vacuous when `loggerMessages === 0` (same pattern as the existing zero-surface check).
- **Selftest** (`runSelfTest`, `scripts/check-telemetry-pii.mjs:560`). Add the scan kind `logmsg: (c) => scanLoggerMessage('selftest.cs', c).findings`.
  - Seeded R7 cases:
    - S25: Message `"sent to {RecipientEmail}"`.
    - S26: parameter `string recipientEmail`.
    - S27: clean declaration + call `LogSent(_logger, user.Email)`.
    - S28: email literal in Message.
    - S29: positional attribute without `Message =`.
    - S30: `public static partial` method.
    - S31: call `LogFailed(_logger, ex, context.Request.Path)`.
  - Clean shapes:
    - C15: attribute + declaration + call with ids, e.g. `{TransferId}` / `transferId`.
    - C16: the same with an `Exception ex` parameter and `LogRoute.Of(context)`.
  - The selftest must print 31/31 seeded and 16/16 clean.
- Update the header doc (R7 paragraph + the "Limitation" lines) to describe the [LoggerMessage] coverage and the private-static convention.

### 2.9 Primary constructors (77 types)
- **Scope:** exactly the types whose only constructor body is `_x = x;` assignments (block or expression-bodied), with no `: base/this(...)`. That is the 77 rows tagged P6. Untouched: the 35 with `?? throw` or computed fields, the 8 base-calling types, and any type with more than one ctor.
- **Convention: keep the readonly fields, initialised from the parameter.** Example: `public sealed partial class GlobalErrorMiddleware(RequestDelegate next, ILogger<GlobalErrorMiddleware> logger)` with `private readonly RequestDelegate _next = next;`.
  - Primary-ctor parameters are mutable captures, so capturing them directly would drop `readonly`.
  - This keeps every `_x` use in the method bodies byte-identical, so the diff is the ctor only.
  - It keeps the P4 `LogX(_logger, …)` calls valid.
  - Members must never use the parameter itself. A parameter used by a member *and* a field initializer raises CS9124, which TWAE makes a build error.
- **XML docs (rule 10):** drop the ctor's "Initializes a new instance…" summary. Move each of its `<param>` tags verbatim into the type's doc comment, after `<summary>`/`<remarks>`. Every primary-ctor parameter needs a `<param>`; CS1573/CS1591 are errors.
- **Inventory note:** the baseline list was derived with IDE0290 plus a body check. P3/P4 change some ctors, so before P6 re-run the check described in P6 step 1 and use the result. Only pure-assignment ctors are converted.

### 2.10 Program.cs split (TA-2.1 `wa.api/Endpoints/`, TA-2.3 "wa.api — endpoint groups … DI composition root", TA-2.4 thin endpoints)
- **Target layout.** TA-2.1 already names `Endpoints/TransferEndpoints.cs, AuthEndpoints.cs, AdminEndpoints.cs`. The DI part mirrors the existing worker precedent `WorkerServices.AddWorkerServices(this IServiceCollection, IConfiguration)`:
  - `src/wa.api/ApiServices.cs`: `public static class ApiServices`, `AddApiServices(this IServiceCollection services, IConfiguration configuration)`, namespace `wa.api`. It contains lines 35-180 verbatim and in the same order. Inside it, `builder.Services.` becomes `services.` and `builder.Configuration` becomes `configuration`. The DI registration order is preserved exactly (last registration wins / enumerable order). Private static helpers per area are allowed only as contiguous slices called in source order.
  - `Program.cs` keeps: the usings, `CreateBuilder`, `builder.Host.UseSerilog(...)`, `builder.Services.AddApiServices(builder.Configuration);`, `Build()`, `var adminGate = AdminGate.FromConfiguration(builder.Configuration);`, the middleware block (lines 207-217) verbatim, the `Map*Endpoints` calls in source order, `app.Run();` and `public partial class Program { }` (`src/wa.api/Program.cs:1532`, which WebApplicationFactory needs).
- **Endpoint classes.** `src/wa.api/Endpoints/*.cs`, namespace `wa.api.Endpoints`, each an `internal static class` with one `public static IEndpointRouteBuilder Map<Area>Endpoints(this IEndpointRouteBuilder endpoints, …)`. Each class holds one **contiguous** source slice, so the endpoint registration order is identical:
  1. System
  2. TransferWrite
  3. PublicTransfer
  4. Unsubscribe
  5. Admin
  6. Auth
  7. MyTransfer
  8. Account
  9. Billing
  10. Feature

  `app.MapX(` becomes `endpoints.MapX(`. Lambdas, route strings, parameter lists, `.RequireFeature(...)` filters and all comments (endpoint-number comments too) move verbatim. **No `MapGroup`:** it would add group metadata and would not shorten any route. Routes, filters, names and order stay identical.
- **Captures become explicit parameters:**
  - `MapSystemEndpoints(bool isDevelopment)`; Program passes `app.Environment.IsDevelopment()`.
  - `MapAdminEndpoints(AdminGate adminGate, IConfiguration configuration)` (line 615 reads config per request).
  - `MapAuthEndpoints(AdminGate adminGate)` (line 1078).
  - `MapBillingEndpoints(IConfiguration configuration)` (line 1436).
  - Program passes `builder.Configuration`, the same live ConfigurationManager instance, read at the same moments: eager reads stay before `Build()` inside `AddApiServices`, per-request reads stay inside the lambdas. WebApplicationFactory overrides keep exactly today's visibility.
- **`AdminGate`** (`internal sealed class`) replaces the local function and its two sets:
  - `static AdminGate FromConfiguration(IConfiguration)` holds the two `foreach` loops of lines 185-190 verbatim. It is called right after `Build()`, at the same point as today.
  - `IReadOnlySet<string> AdminEmails` / `OperatorEmails` are backed by case-insensitive `HashSet`s.
  - `Task<AdminRole> RequireAdminAsync(HttpContext ctx, IAppUserStore users, bool superadminOnly, CancellationToken ct)` keeps the body of lines 195-205, with the sets read from the properties.
  - Line 1078 becomes `AdminRolePolicy.Resolve(result.Email.ToLowerInvariant(), adminGate.AdminEmails, adminGate.OperatorEmails)`. The signature takes `IReadOnlySet<string>` (`src/wa.application/UseCases/Admin/AdminRolePolicy.cs:21`).
- **Helpers:**
  - `ExpiredDetails` / `DownloadLimitDetails` become private static methods in PublicTransferEndpoints.
  - `ParseLongParam` / `ParseDateTimeParam` go to AdminEndpoints.
  - `ToLogoBody` / `ToExportBody` go to AccountEndpoints.
  - `AsUtc` is used by PublicTransfer (434), Admin (766, 769) and Account (1420, 1423). It goes to `EndpointFormatting.AsUtc` (internal static) and its call sites are qualified.
  - All new types and members get rule-10 XML docs.
- **Guard test:** the only test that names Program.cs as a path is `tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs:26`. The FeaturesProvider registration (`src/wa.api/Program.cs:117`) moves to ApiServices, so its allow-list entry changes path.
  - No other guard token occurs in Program.cs: `.PlanId`, plan literals and feature-key literals were checked, and none appear.
  - `scripts/check-telemetry-pii.mjs` scans all of `src/**/*.cs`, so no path list changes.
  - The test comments that mention "Program.cs" (ChaosEndpointTests 54, Login/Unlock/UnsubscribeEndpointTests) are prose. Leave them.

## 3. Implementation Plan

### 3.0 Gate — run at the end of EVERY phase (stop on red, fix inside the phase)
```powershell
dotnet build src/wa.slnx                       # 0 C# warnings (from P2: any warning = error)
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
node scripts/check-telemetry-pii.mjs; node scripts/check-telemetry-pii.mjs selftest
dotnet format src/wa.slnx; git status          # from P1: format must produce NO file changes (local stand-in for CI's verify step)
```
- tests/wa.api.integration is **compiled** by the build. Its tests need Docker, which has been down on this machine. When Docker is up, also run the integration classes a phase touched, filtered (pitfalls.md "Gates"). Otherwise record "integration: CI only" in the commit body. The PR CI from P1 runs the full suite. Never claim integration green from a local run that did not happen.
- The web gates are unaffected: no src/wa.web change. They are not needed per phase.

### P1 — CI on develop + .editorconfig + EnforceCodeStyleInBuild
1. `.github/workflows/ci.yml`: `branches: [main]` becomes `branches: [main, develop]`. Reword the `lint-dotnet` comment (§2.2).
2. Create the root `.editorconfig` with exactly the §4.2 content. Modify `src/.editorconfig`: keep the IDE0130 section; P5/P8 add the rest.
3. Add `EnforceCodeStyleInBuild` to `src/Directory.Build.props`. Create `tests/Directory.Build.props` (§4.1, P1 subset).
4. `dotnet format whitespace src/wa.slnx`. Commit only real content changes: TransferSearchStore, PipelineTests and the 2 final newlines. The 4 CRLF working-tree files (SmokeTests ×2, PingCommandTests, PipelineTests) are LF in the index. `git diff` must show no line-ending-only hunks.
5. Fix the IDE warnings:
   - IDE0040: drop `public` on the two interface consts (IFeaturesProvider 11, ILimitsProvider 13). Interface members are public by default, so this is identical.
   - IDE0044: `readonly` on BlobSasMinterTest 27 and UnsubscribeServiceTests 20-21. Each is assigned only in the ctor or initializer; confirm before changing.
   - IDE1006: HealthFailureTests 23, `factory` → `Factory` (3 uses).
6. Gate. Commit `refactor: CHORE-code-style-2 P1 — CI on develop, editorconfig, style enforcement`.

### P2 — zero compiler warnings + TWAE
1. CS0618 ×49: `new MsSqlBuilder(<same image string>)` and drop `.WithImage(<same>)`, including in the 5 builder chains that span lines.
2. CS8524 ×4: append `_ => throw new SwitchExpressionException(result.State)` as the last arm of the 4 switches. They are at Program.cs 422 (PublicTransferState), 479 (UnlockState), 509 (FileDownloadState) and 533 (DownloadAllState).
3. LoginHandler 58: apply **DECISION-1**.
   - A: `if (user.PasswordHash is null || hasher.VerifyHashedPassword(...) is not (...))`, so the existing failure block runs. Add `tests/wa.application.unit/Auth/LoginHandlerTests.cs` case `Login_AccountWithoutPassword_ThrowsWrongPassword_AndLogsLoginFailed`.
   - B: `user.PasswordHash!` plus a comment that names a follow-up.
   - Unanswered: do everything else and leave this one warning. Do **not** commit P2's TWAE flip until it is answered.
4. Test nullable sites per §2.3. For ZipFunctionCorrelationTests:
   - `InstanceServices` (176): `public override IServiceProvider InstanceServices { get; set; } = null!;`. It is never read; the comment says so.
   - `TraceParent`/`TraceState` (184-185): `=> null!`. This keeps today's runtime value, and the stub feeds TraceContext parsing exactly as before.
   - `BindingData` (190): `IReadOnlyDictionary<string, object?>`.
   - Each `null!` gets a one-line justification comment.
5. Add `TreatWarningsAsErrors` + `WarningsNotAsErrors` (NU1901-1904) to both props files (§4.1).
6. Gate (`dotnet build src/wa.slnx -t:Rebuild`, 0 C# warnings). Commit `… P2 — zero warnings, TreatWarningsAsErrors`.

### P3 — TimeProvider
1. Register `TimeProvider.System` in both roots (§2.6). Replace the `() => DateTimeOffset.UtcNow` seams: Program.cs 94, 95, 98, 155, 161; WorkerServices 62, 63, 157.
2. In each P3-tagged src file: add the ctor param and field, and rewrite each clock read (§2.6). Add `sp.GetRequiredService<TimeProvider>()` to every hand-built `new X(...)` in Program.cs / WorkerServices that the compiler flags. Webhook: Program.cs 1438.
3. Update the doc comment at AdminDeleteTransferHandler 11 (it quotes `DateTime.UtcNow`).
4. Tests: pass `TimeProvider.System` at every flagged construction (29 files). Create ManualTimeProvider and add the 2 proof assertions.
5. Verify: `grep -rnE "DateTime(Offset)?\.UtcNow" src` hits only comments, ideally none.
6. Gate. Commit `… P3 — TimeProvider`.

### P4 — LoggerMessage + PII guard
1. Script first: implement §2.8 and the selftest cases. Run `node scripts/check-telemetry-pii.mjs selftest`. It must be green **before** any conversion. Also prove S27 and S31 fail against a scratch copy (pitfalls "prove it fails").
2. Convert the 80 calls in the 35 P4-tagged files per §2.7. Convert one file at a time and build.
3. `node scripts/check-telemetry-pii.mjs`. The summary must show `logger calls` = 8 (the reporter) plus the new LoggerMessage counts, 80 methods/calls between them, and 0 findings.
4. Grep: no `\.Log(Trace|Debug|Information|Warning|Error|Critical)\(` outside WaTelemetryReporter.
5. Gate. Commit `… P4 — source-generated logging, PII guard covers [LoggerMessage]`.

### P5 — analyzers
1. Add AnalysisLevel to `src/Directory.Build.props`. Add the CA configuration and the reporter/Migrations sections to `src/.editorconfig` (§4.2).
2. Fix the P5-tagged sites (§2.5 table). Apply **DECISION-2** at CorsAllowListMiddleware 40.
   - A: `context.Response.Headers.Append(HeaderNames.Vary, "Origin");`. Add `tests/wa.api.integration/CorsAllowListMiddlewareTests.cs`, which builds the middleware over a DefaultHttpContext with an allowed Origin and asserts `Vary: Origin`. No Docker needed.
   - B: delete the statement and keep the comment as a TODO naming the follow-up.
3. `dotnet build src/wa.slnx -t:Rebuild`: 0 CA hits in src. If a CA rule not listed here appears (SDK 10 versus 11 drift), fix it when it is genuine. Configure it off with a reason only when it is not, and note it in the commit body.
4. `docker build -f src/wa.api/Dockerfile .` if Docker is up (it proves the src-only editorconfig placement). Otherwise rely on the CI `docker` job.
5. Gate. Commit `… P5 — AnalysisLevel 10.0-recommended`.

### P6 — primary constructors
1. Re-derive the list. The types with exactly one instance ctor, no base/this call, and a body made only of `_x = x;` statements, among the IDE0290 hits (`dotnet build src/wa.slnx -p:EnforceCodeStyleInBuild=true` with `dotnet_diagnostic.IDE0290.severity = warning` set temporarily in a **scratch copy**, or by reading). Expected 77 ± the P3 changes.
2. Convert each type per §2.9: header params, field initializers, ctor removed, `<param>` tags moved to the type doc.
3. Gate (CS9124/CS1573 surface as errors). Commit `… P6 — primary constructors`.

### P7 — Program.cs split
1. Create `ApiServices.cs` and move the DI block (§2.10).
2. Create AdminGate, EndpointFormatting and the 10 endpoint classes. Move the slices verbatim, in order.
3. Rewrite Program.cs to the §4.4 shape.
4. Update the PlanFeaturesConsumerGuardTests allow-list (line 26).
5. **Moved-code identity check (scratch, not committed):**
   - (a) The ordered list of `Map(Get|Post|Put|Patch|Delete)("<route>"` from `git show HEAD:src/wa.api/Program.cs` must equal the same list taken from the new endpoint files concatenated in map-call order.
   - (b) `.RequireFeature(` count: 2 before and after.
   - (c) Normalise both sides (strip leading whitespace, `app.`/`endpoints.` prefix) and diff the moved bodies. The only allowed differences are `adminGate.` / `configuration.` / `EndpointFormatting.` qualification.
   - (d) Use `git diff` in color-moved mode to eyeball it.
6. Gate. Run the integration classes for every area if Docker is up, otherwise rely on CI. Commit `… P7 — split Program.cs into ApiServices + Endpoints`.

### P8 — IDE0005 sweep + final verification
1. Add `dotnet_diagnostic.IDE0005.severity = warning` to `src/.editorconfig` `[*.cs]`. Then `dotnet format src/wa.slnx`: with everything else clean, it removes only the unnecessary usings. Review the diff; expect around 158 lines in 142 files plus the P7 files.
2. Full gate plus `dotnet build src/wa.slnx -t:Rebuild`. Commit `… P8 — remove unnecessary usings`. Hand over for PR → develop. CI must be 9/9 green, integration included.

### FILES FOR CODER

Phase tags: **P1…P8** = the phase that changes the file (§3). One row per file, all phases listed. Not parsed by `scripts/verify_plan.py` (outside src/tests/scripts): `.github/workflows/ci.yml` MODIFY (P1 trigger + comment) and the repo-root `.editorconfig` CREATE (P1, §4.2). Conditional rows (only if the DECISION answer is A): `tests/wa.api.integration/CorsAllowListMiddlewareTests.cs` CREATE (DECISION-2) and `tests/wa.application.unit/Auth/LoginHandlerTests.cs` MODIFY (DECISION-1). No file is deleted or moved.

| File | Action | What to change |
|---|---|---|
| `src/.editorconfig` | MODIFY | P1 keep IDE0130 section; P5 CA configuration + reporter section + Migrations generated_code; P8 IDE0005 warning (target content in 4.2) |
| `src/Directory.Build.props` | MODIFY | P1 EnforceCodeStyleInBuild; P2 TreatWarningsAsErrors + NU1901-NU1904 not-as-errors; P5 AnalysisLevel 10.0-recommended (target content in 4.1) |
| `src/wa.application/Limits/Interfaces/IFeaturesProvider.cs` | MODIFY | P1 IDE0040: drop the redundant public on the interface const (line 11); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/Limits/Interfaces/ILimitsProvider.cs` | MODIFY | P1 IDE0040: drop the redundant public on the interface const (line 13); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/DeleteExpiredTransfersHandler.cs` | MODIFY | P1 final newline; P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 31) |
| `src/wa.domain/LimitsRecord.cs` | MODIFY | P1 final newline |
| `src/wa.infrastructure/Persistence/TransferSearchStore.cs` | MODIFY | P1 dotnet format whitespace (9 WHITESPACE sites, lines 28-52); P6 primary constructor (ctor at baseline line 19) |
| `tests/Directory.Build.props` | CREATE | P1 EnforceCodeStyleInBuild; P2 TreatWarningsAsErrors (content in 4.1) |
| `tests/wa.api.integration/HealthFailureTests.cs` | MODIFY | P1 IDE1006: rename static field factory to Factory (3 uses, line 23) |
| `tests/wa.api.integration/PipelineTests.cs` | MODIFY | P1 IDE0055 whitespace lines 33-34 |
| `tests/wa.application.unit/BlobSasMinterTest.cs` | MODIFY | P1 IDE0044 readonly field (line 27) |
| `tests/wa.application.unit/Transfers/UnsubscribeServiceTests.cs` | MODIFY | P1 IDE0044 readonly fields (lines 20-21); P3 pass TimeProvider.System to UnsubscribeService |
| `src/wa.api/Program.cs` | MODIFY | P2 CS8524 x4 (lines 422, 479, 509, 533); P3 TimeProvider registration + clock seams + webhook clock; P5 CA2016 x4 + CA1305 line 29; P7 split into ApiServices + Endpoints; P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/LoginHandler.cs` | MODIFY | P2 CS8604 line 58, per DECISION-1; P6 primary constructor (ctor at baseline line 28) |
| `tests/wa.api.integration/AdminErasuresEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/AdminFlagsEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/AdminOverviewEmailHealthTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/AdsFlagToggleTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to SendTransferHandler |
| `tests/wa.api.integration/BillingEmailSequenceIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/BillingGraceIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/CheckoutEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P6 primary constructor (ctor at baseline line 201) |
| `tests/wa.api.integration/DataExportEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/DataExportJobIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/DeleteTransferPurgesAnalyticsTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/DeleteTransfersJobIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to ExpiryDeletionStore, SbEventPublisher |
| `tests/wa.api.integration/DownloadAllUrlEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/DownloadUrlEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/DraftEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P2 CS8604 lines 91, 114: GetString()! (same idiom as line 113) |
| `tests/wa.api.integration/EmailBrandingIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to SendNotificationEmailsHandler |
| `tests/wa.api.integration/EmailNotificationsSettingTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/ErasureReportIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to GdprSweepStore |
| `tests/wa.api.integration/Events/DegradedEmailSeamTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to SendNotificationEmailsHandler |
| `tests/wa.api.integration/Events/OutboxRetryTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to SbEventPublisher |
| `tests/wa.api.integration/ExpiryJobIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to ExpiryStore, SbEventPublisher |
| `tests/wa.api.integration/FeatureGatesEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/FinalizeAccountLimitsTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/FinalizeEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/FlagOverrideIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/GdprSweepIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/LoginEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/LoginReflectsPlanChangeTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/OrgLogoUploadTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/PlanCatalogIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/PlanContextIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/PlanUsageEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/PortalEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P6 primary constructor (ctor at baseline line 195) |
| `tests/wa.api.integration/PublicTransferAdsTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/PublicTransferBrandingTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/PublicTransferEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P2 CS8631 line 274: GetString()! inside the Select |
| `tests/wa.api.integration/ResendMeterNoDoubleCountTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/ResendSendDetachTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/ScheduledSendEndpointTestsBase.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/ScheduledSendTimerIntegrationTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P3 pass TimeProvider.System to ExpiryStore, PublicTransferStore, SbEventPublisher, SendNotificationEmailsHandler |
| `tests/wa.api.integration/SeedDataIntegrationTest.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/SendEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/SignupEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image); P2 CS8604 line 80: row!.PasswordHash! |
| `tests/wa.api.integration/StripeWebhookEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/TransferAnalyticsEndpointTestsBase.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/TransferSearchEndpointTestsBase.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/UnlockEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.api.integration/UnsubscribeEndpointTests.cs` | MODIFY | P2 CS0618: new MsSqlBuilder(image) instead of new MsSqlBuilder().WithImage(image) |
| `tests/wa.application.unit/Admin/AdminDeleteTransferCommandTests.cs` | MODIFY | P2 CS8619 line 212: Task.FromResult<MarkDeleteResult?>(..); P3 pass TimeProvider.System to AdminDeleteTransferHandler; P3 ManualTimeProvider proof: store receives fixed now (UtcDateTime) |
| `tests/wa.application.unit/Auth/RedeemMagicLinkTests.cs` | MODIFY | P2 CS8604 line 85: _tokens.Raw! (same idiom as line 102); P3 pass TimeProvider.System to RedeemMagicLinkHandler |
| `tests/wa.application.unit/Transfers/DeleteTransferCommandTests.cs` | MODIFY | P2 CS8619 line 258: Task.FromResult<MarkDeleteResult?>(..); P3 pass TimeProvider.System to DeleteTransferHandler |
| `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs` | MODIFY | P2 CS8620 line 38: new Dictionary<string, string?>; P3 pass TimeProvider.System to SendNotificationEmailsHandler |
| `tests/wa.workers.unit/ZipFunctionCorrelationTests.cs` | MODIFY | P2 CS8764 x3 + CS8609 at lines 176, 184, 185, 190 (P2 step 4) |
| `src/wa.api/Middleware/IdempotencyMiddleware.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P5 CA1835 lines 92, 98: ReadOnlyMemory overload; P6 primary constructor (ctor at baseline line 29) |
| `src/wa.api/Middleware/SessionMiddleware.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/AdminDeleteTransferHandler.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s) |
| `src/wa.application/UseCases/Admin/AdminForceDeleteUserHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.application/UseCases/Admin/AuditWriter.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.application/UseCases/Admin/SetFlagCommandHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/DeleteAccountHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 30); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/ForgotPasswordHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P5 CA1850 line 105: SHA256.HashData; P6 primary constructor (ctor at baseline line 32) |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkHandler.cs` | MODIFY | P3 inject TimeProvider, replace 3 clock read(s); P5 CA1850 line 116; P6 primary constructor (ctor at baseline line 32) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P5 CA1850 line 94; P6 primary constructor (ctor at baseline line 30) |
| `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs` | MODIFY | P3 inject TimeProvider, replace 4 clock read(s); P4 3 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P5 CA1305 line 176: ToString(CultureInfo.InvariantCulture) |
| `src/wa.application/UseCases/Auth/SendMagicLinkEmailHandler.cs` | MODIFY | P3 inject TimeProvider, replace 4 clock read(s); P4 3 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P5 CA1305 line 173 |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookHandler.cs` | MODIFY | P3 inject TimeProvider, replace 4 clock read(s); P4 5 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P4 4 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P5 CA1305 line 166 |
| `src/wa.application/UseCases/DataExport/ExportMyDataHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P6 primary constructor (ctor at baseline line 16) |
| `src/wa.application/UseCases/DataExport/GetDataExportHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P6 primary constructor (ctor at baseline line 16) |
| `src/wa.application/UseCases/Transfers/ActivateScheduledTransfersHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 25) |
| `src/wa.application/UseCases/Transfers/DeleteTransferHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.application/UseCases/Transfers/FinalizeTransferHandler.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s) |
| `src/wa.application/UseCases/Transfers/GetFileDownloadUrlHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P6 primary constructor (ctor at baseline line 31); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/GetTransferAnalyticsHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.application/UseCases/Transfers/ResendTransferHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs` | MODIFY | P3 inject TimeProvider, replace 4 clock read(s); P4 5 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 43) |
| `src/wa.application/UseCases/Transfers/SendTransferHandler.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.infrastructure/Blob/BlobSasMinter.cs` | MODIFY | P3 keep the Func<DateTimeOffset> seam; its default becomes TimeProvider.System.GetUtcNow |
| `src/wa.infrastructure/Events/InMemoryEventPublisher.cs` | MODIFY | P3 keep the Func<DateTimeOffset> seam; its default becomes TimeProvider.System.GetUtcNow |
| `src/wa.infrastructure/Events/SbEventPublisher.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.infrastructure/Persistence/BlobDeletionStore.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/DataExportJobStore.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/ExpiryStore.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/GdprSweepStore.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s) |
| `src/wa.infrastructure/Persistence/IdempotencyStore.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P6 primary constructor (ctor at baseline line 18) |
| `src/wa.infrastructure/Persistence/PublicTransferStore.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/UnsubscribeService.cs` | MODIFY | P3 inject TimeProvider, replace 1 clock read(s); P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 22) |
| `src/wa.workers/DataExportFunction.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 25) |
| `src/wa.workers/DeleteBlobsFunction.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 26) |
| `src/wa.workers/DeleteTransfersFunction.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 26) |
| `src/wa.workers/ExpireFunction.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P4 5 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 33) |
| `src/wa.workers/GdprSweepFunction.cs` | MODIFY | P3 inject TimeProvider, replace 2 clock read(s); P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 24) |
| `src/wa.workers/WorkerServices.cs` | MODIFY | P3 register TimeProvider.System; clock seams at lines 62, 63, 157 |
| `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs` | MODIFY | P3 pass TimeProvider.System to PublicTransferStore |
| `tests/wa.application.unit/Admin/AdminForceDeleteUserCommandTests.cs` | MODIFY | P3 pass TimeProvider.System to AdminForceDeleteUserHandler |
| `tests/wa.application.unit/Admin/AuditWriterTests.cs` | MODIFY | P3 pass TimeProvider.System to AuditWriter |
| `tests/wa.application.unit/Admin/SetFlagCommandTests.cs` | MODIFY | P3 pass TimeProvider.System to SetFlagCommandHandler |
| `tests/wa.application.unit/Auth/ForgotPasswordTests.cs` | MODIFY | P3 pass TimeProvider.System to ForgotPasswordHandler |
| `tests/wa.application.unit/Auth/MeProfileTests.cs` | MODIFY | P3 pass TimeProvider.System to DeleteAccountHandler |
| `tests/wa.application.unit/Auth/RequestMagicLinkTests.cs` | MODIFY | P3 pass TimeProvider.System to RequestMagicLinkHandler |
| `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs` | MODIFY | P3 pass TimeProvider.System to SendBillingEmailHandler |
| `tests/wa.application.unit/ManualTimeProvider.cs` | CREATE | P3 hand-written TimeProvider test double (no package) |
| `tests/wa.application.unit/Transfers/ActivateScheduledTransfersHandlerTests.cs` | MODIFY | P3 pass TimeProvider.System to ActivateScheduledTransfersHandler |
| `tests/wa.application.unit/Transfers/FinalizeTransferHandlerTests.cs` | MODIFY | P3 pass TimeProvider.System to FinalizeTransferHandler |
| `tests/wa.application.unit/Transfers/GetFileDownloadUrlHandlerTests.cs` | MODIFY | P3 pass TimeProvider.System to GetFileDownloadUrlHandler; P3 ManualTimeProvider proof: ExpiresAt == fixed now + 30 min |
| `tests/wa.application.unit/Transfers/ResendFilesGoneTests.cs` | MODIFY | P3 pass TimeProvider.System to ResendTransferHandler |
| `tests/wa.application.unit/Transfers/ResendTransferHandlerTests.cs` | MODIFY | P3 pass TimeProvider.System to ResendTransferHandler |
| `tests/wa.application.unit/Transfers/SendTransferFilesGoneTests.cs` | MODIFY | P3 pass TimeProvider.System to SendTransferHandler |
| `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs` | MODIFY | P3 pass TimeProvider.System to SendTransferHandler |
| `tests/wa.application.unit/Transfers/SendTransferSupersedeTests.cs` | MODIFY | P3 pass TimeProvider.System to SendTransferHandler |
| `scripts/check-telemetry-pii.mjs` | MODIFY | P4 R7 covers [LoggerMessage] templates, parameters and call sites; selftest S25-S31 / C15-C16 |
| `src/wa.api/Middleware/GlobalErrorMiddleware.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 20); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/SignupHandler.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 31) |
| `src/wa.application/UseCases/Billing/EndBillingGraceHandler.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.application/UseCases/Billing/SendBillingEmailsHandler.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.application/UseCases/DataExport/BuildDataExportsHandler.cs` | MODIFY | P4 3 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 22) |
| `src/wa.application/UseCases/EmailSettings/OwnerNotificationGate.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 21) |
| `src/wa.application/UseCases/Gdpr/SweepErasedTelemetryHandler.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 20) |
| `src/wa.application/UseCases/Health/HealthQueryHandler.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 18) |
| `src/wa.application/UseCases/Transfers/CreateDraftHandler.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 25) |
| `src/wa.application/UseCases/Transfers/DeleteOrphanBlobsHandler.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 22) |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P5 CA2016 line 110: explicit CancellationToken.None; P6 primary constructor (ctor at baseline line 38); P8 IDE0005 x1 (dotnet format) |
| `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` | MODIFY | P4 7 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.infrastructure/Blob/AzureBlobStore.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.infrastructure/Blob/BlobContainerBootstrap.cs` | MODIFY | P4 5 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 40) |
| `src/wa.infrastructure/Email/LogEmailSender.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 16) |
| `src/wa.infrastructure/Email/StubbleTemplateRenderer.cs` | MODIFY | P4 2 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial |
| `src/wa.workers/EmailFunction.cs` | MODIFY | P4 1 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P6 primary constructor (ctor at baseline line 39) |
| `src/wa.workers/ZipFunction.cs` | MODIFY | P4 3 logger call(s) to [LoggerMessage] private static partial methods; type becomes partial; P5 CA1822 line 197: make VerifySignature static; P6 primary constructor (ctor at baseline line 40) |
| `src/wa.api/Middleware/CorsAllowListMiddleware.cs` | MODIFY | P5 CA1806 line 40, per DECISION-2; P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SearchTransfersQueryHandler.cs` | MODIFY | P5 CA1859 line 73 (private member type) |
| `src/wa.application/UseCases/Auth/UpdateProfileHandler.cs` | MODIFY | P5 CA1305 line 66; P6 primary constructor (ctor at baseline line 36); P8 IDE0005 x2 (dotnet format) |
| `src/wa.domain/Analytics/DownloadAnalytics.cs` | MODIFY | P5 CA1859 line 138 |
| `src/wa.domain/EventTypeConverter.cs` | MODIFY | P5 CA2263 line 40: Enum.GetValues<EventType>() |
| `src/wa.domain/PlanDefinition.cs` | MODIFY | P5 CA1859 line 183 |
| `src/wa.infrastructure/Persistence/SendStore.cs` | MODIFY | P5 CA1859 line 139; P6 primary constructor (ctor at baseline line 20) |
| `src/wa.infrastructure/Persistence/WaDbContext.cs` | MODIFY | P5 CA2263 line 26: Conventions.Remove<ForeignKeyIndexConvention>() |
| `src/wa.api/Middleware/CorrelationMiddleware.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.api/Middleware/PlanContextMiddleware.cs` | MODIFY | P6 primary constructor (ctor at baseline line 16); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/Telemetry/WaTelemetryReporter.cs` | MODIFY | P6 primary constructor (ctor at baseline line 24) |
| `src/wa.application/UseCases/Auth/GetOrgLogoHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 18) |
| `src/wa.application/UseCases/Auth/GetProfileHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 21); P8 IDE0005 x4 (dotnet format) |
| `src/wa.application/UseCases/Auth/RemoveOrgLogoHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 22); P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/UploadOrgLogoHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 32) |
| `src/wa.application/UseCases/Transfers/ExpireTransfersHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 27) |
| `src/wa.application/UseCases/Transfers/GetMyTransferHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 15) |
| `src/wa.application/UseCases/Transfers/GetPublicTransferHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 42) |
| `src/wa.application/UseCases/Transfers/ListMyTransfersHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.application/UseCases/Transfers/UnlockTransferHandler.cs` | MODIFY | P6 primary constructor (ctor at baseline line 26) |
| `src/wa.infrastructure/Persistence/AdminFlagStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/AdminSuppressionStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/AdminTransferStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 19) |
| `src/wa.infrastructure/Persistence/AdminUserStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 18) |
| `src/wa.infrastructure/Persistence/AppDbContextFactory.cs` | MODIFY | P6 primary constructor (ctor at baseline line 15) |
| `src/wa.infrastructure/Persistence/AppUserStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/AuditReadStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/AuditStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17); P8 IDE0005 x1 (dotnet format) |
| `src/wa.infrastructure/Persistence/AuthTokenStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/EmailNotificationsStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 14) |
| `src/wa.infrastructure/Persistence/EmailStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 18) |
| `src/wa.infrastructure/Persistence/FlagsStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 14) |
| `src/wa.infrastructure/Persistence/OrgLogoStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/OrgNameStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 14) |
| `src/wa.infrastructure/Persistence/OverviewStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 17) |
| `src/wa.infrastructure/Persistence/PlansStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 14) |
| `src/wa.infrastructure/Persistence/SearchTransfersStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 18) |
| `src/wa.infrastructure/Persistence/SenderBrandingStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 14) |
| `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 18) |
| `src/wa.infrastructure/Persistence/TransferStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 18) |
| `src/wa.infrastructure/Persistence/UserPlanStore.cs` | MODIFY | P6 primary constructor (ctor at baseline line 14) |
| `src/wa.infrastructure/Persistence/WaDbContextFactory.cs` | MODIFY | P6 primary constructor (ctor at baseline line 12) |
| `src/wa.infrastructure/Persistence/ZipFileSource.cs` | MODIFY | P6 primary constructor (ctor at baseline line 16) |
| `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs` | MODIFY | P6 primary constructor (ctor at baseline line 679) |
| `src/wa.api/ApiServices.cs` | CREATE | P7 AddApiServices(IServiceCollection, IConfiguration): the Program.cs DI block (lines 35-180), same order |
| `src/wa.api/Endpoints/AccountEndpoints.cs` | CREATE | P7 DELETE me, logo x3, export x2 + ToLogoBody / ToExportBody (lines 1304-1427) |
| `src/wa.api/Endpoints/AdminEndpoints.cs` | CREATE | P7 the 13 admin routes + ParseLongParam / ParseDateTimeParam (lines 563-930) |
| `src/wa.api/Endpoints/AdminGate.cs` | CREATE | P7 admin/operator email sets + RequireAdminAsync (Program.cs lines 183-205) |
| `src/wa.api/Endpoints/AuthEndpoints.cs` | CREATE | P7 signup, magic-link, forgot-password, redeem, login, GET/PATCH me, logout (lines 931-1153) |
| `src/wa.api/Endpoints/BillingEndpoints.cs` | CREATE | P7 Stripe webhook, checkout, portal, plan (lines 1428-1523) |
| `src/wa.api/Endpoints/EndpointFormatting.cs` | CREATE | P7 shared AsUtc helper (Program.cs line 1416) |
| `src/wa.api/Endpoints/FeatureEndpoints.cs` | CREATE | P7 GET /api/v1/features (lines 1524-1527) |
| `src/wa.api/Endpoints/MyTransferEndpoints.cs` | CREATE | P7 GET list, GET by id, analytics, DELETE (lines 1154-1303) |
| `src/wa.api/Endpoints/PublicTransferEndpoints.cs` | CREATE | P7 public GET, unlock, download-url, download-all-url + ExpiredDetails / DownloadLimitDetails (lines 416-552) |
| `src/wa.api/Endpoints/SystemEndpoints.cs` | CREATE | P7 "/", /health, dev chaos (lines 218-253) |
| `src/wa.api/Endpoints/TransferWriteEndpoints.cs` | CREATE | P7 draft, finalize, send, resend (lines 254-415) |
| `src/wa.api/Endpoints/UnsubscribeEndpoints.cs` | CREATE | P7 GET /unsubscribe/{token} (lines 553-562) |
| `tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs` | MODIFY | P7 allow-list entry src/wa.api/Program.cs becomes src/wa.api/ApiServices.cs (line 26) |
| `src/wa.api/Middleware/FeatureGateEndpointFilter.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.api/Middleware/LogRoute.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.api/Middleware/ProblemWriter.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/Limits/Interfaces/IFlagResolver.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/Limits/Interfaces/IPlanContextAccessor.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/AdminDeleteTransferCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/AdminForceDeleteResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/AdminForceDeleteUserCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/ErasureListItem.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/ErasureReportPolicy.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/FlagView.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/GetErasuresQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/GetErasuresResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/GetFlagsQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/GetUsersResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/Interfaces/IAuditWriter.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SearchTransfersQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SearchTransfersResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SearchTransfersValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetFlagCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetFlagException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetFlagResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetUserPlanCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetUserPlanCommandHandler.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetUserPlanException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Admin/SetUserPlanResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/DeleteAccountCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/DeleteAccountResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/ForgotPasswordCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/ForgotPasswordResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/ForgotPasswordValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/GetOrgLogoQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/GetProfileCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/GetProfileResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/Interfaces/ISessionTokenMinter.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/LinkExpiredException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/LoginCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/LoginResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/LoginValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/LoginWrongPasswordException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/MeProfileException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/OrgLogoRejectedException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/OrgLogoView.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/OrgNamePatch.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/OrgNamePatchJsonConverter.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/RedeemMagicLinkResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/RedeemValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/RemoveOrgLogoCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/RequestMagicLinkValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/SessionTokenService.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/SignupCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/SignupResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/SignupValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/UpdateProfileCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Auth/UpdateProfileRequest.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Auth/UploadOrgLogoCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/CheckoutException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/CheckoutRequest.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/PlanUsageResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/PortalSessionResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/StripeWebhookException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Billing/StripeWebhookOutcome.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/DataExport/DataExportInFlightException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/DataExport/DataExportPolicy.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/DataExport/DataExportView.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/DataExport/ExportMyDataCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/DataExport/GetDataExportQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Health/HealthQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Health/HealthResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ActiveTransfersExceededException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/AnalyticsForbiddenException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/BlobSizeMismatchException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/CreateDraftCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/CreateDraftFile.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/CreateDraftResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/DeleteTransferCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/DownloadAllResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/DownloadAllState.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/DraftFile.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FileDownloadResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FileDownloadState.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FileItemRow.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FilesGoneException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FinalizeFile.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FinalizeTransferCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FinalizeTransferResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/FinalizedFile.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/GetFileDownloadUrlQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/GetPublicTransferResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/Interfaces/ISendStore.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Transfers/Interfaces/IUnlockTokenMinter.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ListMyTransfersResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/MarkDeleteResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/MyTransferRow.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/PublicFileItem.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/PublicTransferState.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ResendFileItem.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ResendForbiddenException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ResendTransferCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ResendTransferResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ScheduleForbiddenException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/ScheduleOutOfWindowException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/SearchMyTransfersQuery.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/SendTransferCommand.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.application/UseCases/Transfers/SendTransferResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/StorageQuotaExceededException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/TransferValidationException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/UnlockPasswordMissingException.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/UnlockState.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/UnlockTransferCommand.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.application/UseCases/Transfers/UnlockTransferResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.domain/BillingInterval.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.domain/EventEnvelope.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.domain/EventType.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.infrastructure/Blob/SasLogMask.cs` | MODIFY | P8 IDE0005 x2 (dotnet format) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.infrastructure/Persistence/UnsubscribeResult.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `src/wa.workers/Program.cs` | MODIFY | P8 IDE0005 x1 (dotnet format) |
| `tests/wa.api.integration/CorsAllowListMiddlewareTests.cs` | CREATE | P5, only if DECISION-2 = A: allowed Origin over DefaultHttpContext asserts the Vary: Origin header |
| `tests/wa.application.unit/Auth/LoginHandlerTests.cs` | MODIFY | P2, only if DECISION-1 = A: account without a password hash throws wrong-password and logs login_failed |

#### 4.1 Props files (target content)
`src/Directory.Build.props`. Today it holds `GenerateDocumentationFile` + the doc `WarningsAsErrors`. Keep both and add, inside the same `.csproj`-conditioned PropertyGroup:
```xml
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>                                 <!-- P1 -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>                                     <!-- P2 -->
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors> <!-- P2: package-audit CVEs stay warnings -->
    <AnalysisLevel>10.0-recommended</AnalysisLevel>                                        <!-- P5: pinned, see plan 2.5 -->
```
`tests/Directory.Build.props` (new; tests do not inherit src/; no AnalysisLevel, no XML docs):
```xml
<Project>
  <!-- CHORE-code-style-2: tests compile warning-free with enforced style; analyzers stay src-only (CA1707 vs xUnit names). -->
  <PropertyGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

#### 4.2 Editorconfig (target content — validated on a scratch copy of the tree)
Repo root `.editorconfig` (P1). Add a one-line `#` reason above each group:
```ini
root = true

[*]
end_of_line = lf
insert_final_newline = true
indent_style = space

[*.cs]
indent_size = 4
charset = utf-8
trim_trailing_whitespace = true
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_indent_case_contents = true
csharp_indent_switch_labels = true
dotnet_sort_system_directives_first = false
dotnet_separate_import_directive_groups = false
csharp_style_namespace_declarations = file_scoped:warning
csharp_using_directive_placement = outside_namespace:warning
csharp_preferred_modifier_order = public,private,protected,internal,file,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,required,volatile,async:warning
dotnet_style_require_accessibility_modifiers = for_non_interface_members:warning
dotnet_style_readonly_field = true:warning
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_prefer_braces = when_multiline:silent
dotnet_diagnostic.IDE0055.severity = warning
dotnet_diagnostic.IDE0161.severity = warning
dotnet_diagnostic.IDE0065.severity = warning
dotnet_diagnostic.IDE0036.severity = warning
dotnet_diagnostic.IDE0040.severity = warning
dotnet_diagnostic.IDE0044.severity = warning
dotnet_diagnostic.IDE1006.severity = warning
dotnet_diagnostic.IDE0290.severity = suggestion

dotnet_naming_symbols.interfaces.applicable_kinds = interface
dotnet_naming_style.i_prefix.required_prefix = I
dotnet_naming_style.i_prefix.capitalization = pascal_case
dotnet_naming_rule.interfaces_i_prefix.symbols = interfaces
dotnet_naming_rule.interfaces_i_prefix.style = i_prefix
dotnet_naming_rule.interfaces_i_prefix.severity = warning

dotnet_naming_symbols.constants.applicable_kinds = field
dotnet_naming_symbols.constants.applicable_accessibilities = *
dotnet_naming_symbols.constants.required_modifiers = const
dotnet_naming_style.pascal.capitalization = pascal_case
dotnet_naming_rule.constants_pascal.symbols = constants
dotnet_naming_rule.constants_pascal.style = pascal
dotnet_naming_rule.constants_pascal.severity = warning

dotnet_naming_symbols.private_static_readonly.applicable_kinds = field
dotnet_naming_symbols.private_static_readonly.applicable_accessibilities = private
dotnet_naming_symbols.private_static_readonly.required_modifiers = static, readonly
dotnet_naming_rule.private_static_readonly_pascal.symbols = private_static_readonly
dotnet_naming_rule.private_static_readonly_pascal.style = pascal
dotnet_naming_rule.private_static_readonly_pascal.severity = warning

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_style.underscore_camel.capitalization = camel_case
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel
dotnet_naming_rule.private_fields_underscore.severity = warning
```
`src/.editorconfig`. Existing content, kept verbatim as the first section:
```ini
# CHORE-code-style §2.3: interfaces live in Interfaces/ folders but keep their area namespace (no .Interfaces suffix).
[**/Interfaces/*.cs]
dotnet_style_namespace_match_folder = false
```
Appended. This must live in src/ because the Docker builds copy only src/ (plan §2.4):
```ini
[*.cs]
dotnet_diagnostic.CA1725.severity = none   # P5: ct / b parameter abbreviations are the repo convention; no named-argument callers
dotnet_diagnostic.CA1716.severity = none   # P5: VB keyword clash only; no VB consumers
dotnet_diagnostic.CA1711.severity = none   # P5: IEmailEventHandler is a handler contract by design
dotnet_diagnostic.CA2007.severity = none   # P5: no SynchronizationContext in ASP.NET Core / Functions
dotnet_diagnostic.IDE0005.severity = warning  # P8 only: needs GenerateDocumentationFile (src-only)

[wa.application/Telemetry/WaTelemetryReporter.cs]
# P5: the single telemetry exit (AC-056-4) — templates are the TA-10.2 wire contract, LogEvent's template is the event name by design,
# lower-case placeholders are TA-10.2 property names, FileNameSlot is instance API keyed on by the PII guard (R4).
dotnet_diagnostic.CA1848.severity = none
dotnet_diagnostic.CA1873.severity = none
dotnet_diagnostic.CA2254.severity = none
dotnet_diagnostic.CA1727.severity = none
dotnet_diagnostic.CA1822.severity = none

[wa.infrastructure/Persistence/Migrations/**.cs]
# P5: EF-generated (BOM, array literals) — not linted.
generated_code = true
charset = unset
```

#### 4.3 Existing code the Coder changes (non-obvious sites only)
`src/wa.application/UseCases/Auth/LoginHandler.cs:56` (DECISION-1):
```csharp
        var hasher = new PasswordHasher<string>();
        if (hasher.VerifyHashedPassword(user.Id.ToString("n"), user.PasswordHash, request.Password) is not
            (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
        {
            _telemetry.LogEvent(TelemetryEvents.LoginFailed); // AC-008-1 / task §4: login_failed on the wrong-password path
            throw LoginWrongPasswordException.ForWrongPassword(); // failed verify → same shape as no row (no-leak)
```
`src/wa.api/Middleware/CorsAllowListMiddleware.cs:40` (DECISION-2). `Vary` is a StringValues, so `.Append` binds to LINQ `Enumerable.Append` and the result is discarded:
```csharp
            context.Response.Headers.Vary.Append("Origin"); // echoed responses vary by origin — … set on every response, not just preflight
```
`tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:176`, the stub overrides at lines 176, 184-185 and 190:
```csharp
        public override IServiceProvider? InstanceServices { get; set; }
        public override string? TraceParent => null;
        public override string? TraceState => null;
        public override IReadOnlyDictionary<string, object> BindingData => new Dictionary<string, object>();
```
Logging conversion pattern (GlobalErrorMiddleware, baseline lines 39 and 44):
```csharp
            _logger.LogWarning("Business error {Code} (HTTP {Status})", business.Code, business.Status);
            _logger.LogError(unhandled, "Unhandled exception on {Route} — mapped to 500 INTERNAL", LogRoute.Of(context));
```
becomes
```csharp
            LogBusinessError(_logger, business.Code, business.Status);
            LogUnhandled(_logger, unhandled, LogRoute.Of(context));
    // …
    [LoggerMessage(Level = LogLevel.Warning, Message = "Business error {Code} (HTTP {Status})")]
    private static partial void LogBusinessError(ILogger logger, string code, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Route} — mapped to 500 INTERNAL")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string route);
```
The parameter types must equal the argument types; check `BusinessException.Code` / `.Status` in `src/wa.api/Middleware/BusinessException.cs`.

Primary ctor pattern (same file, baseline ctor at line 20):
```csharp
/// <summary>…unchanged…</summary>
/// <param name="next">Next middleware in the pipeline.</param>
/// <param name="logger">Logger for failures; logs route templates and business codes, never raw paths or messages.</param>
public sealed partial class GlobalErrorMiddleware(RequestDelegate next, ILogger<GlobalErrorMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalErrorMiddleware> _logger = logger;
```
`src/wa.api/Program.cs:195`, the local function that becomes `AdminGate.RequireAdminAsync`. Its sets are built at lines 185-190 and also read at line 1078:
```csharp
async Task<AdminRole> RequireAdminAsync(HttpContext ctx, IAppUserStore users, bool superadminOnly, CancellationToken ct)
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        throw new BusinessException(ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated));
    var user = await users.GetByIdAsync(userId, ct);
    var role = user?.Email is { } email ? AdminRolePolicy.Resolve(email.ToLowerInvariant(), adminEmails, operatorEmails) : null;
    if (role is null || (superadminOnly && role != AdminRole.SuperAdmin))
        throw new BusinessException(ProblemCodes.Forbidden, ProblemCodes.TitleOf(ProblemCodes.Forbidden));
    return role.Value;
}
```
PII guard: `scanLog` in `scripts/check-telemetry-pii.mjs:338` holds the argument checks (email literal, placeholders, `stripSanctioned` identifiers, `.Name`, `.Path/.PathBase/.QueryString/.Message`). Factor these into `logArgReasons` and reuse them for the [LoggerMessage] call sites (§2.8).

#### 4.4 Program.cs target shape (after P7)
```csharp
// usings (IDE0005 trims them in P8) + using wa.api; using wa.api.Endpoints;
var builder = WebApplication.CreateBuilder(args);
// TA-10.5 comment + builder.Host.UseSerilog(...) — verbatim (P5: formatProvider: CultureInfo.CurrentCulture)
builder.Services.AddApiServices(builder.Configuration);
var app = builder.Build();
var adminGate = AdminGate.FromConfiguration(builder.Configuration); // T-011-01 comments move with the sets into AdminGate
// middleware block — lines 207-217 verbatim, same order
app.MapSystemEndpoints(app.Environment.IsDevelopment());
app.MapTransferWriteEndpoints();
app.MapPublicTransferEndpoints();
app.MapUnsubscribeEndpoints();
app.MapAdminEndpoints(adminGate, builder.Configuration);
app.MapAuthEndpoints(adminGate);
app.MapMyTransferEndpoints();
app.MapAccountEndpoints();
app.MapBillingEndpoints(builder.Configuration);
app.MapFeatureEndpoints();
app.Run();

/// <summary>Empty partial class so WebApplicationFactory&lt;Program&gt; can resolve the entry point.</summary>
public partial class Program { }
```

## 5. Open Questions / Risks

### Escalations (docs/AGENT.md §8)
```
DECISION: Should login treat an account without a password hash (magic-link-only) as WRONG_PASSWORD instead of letting PasswordHasher throw?
CONTEXT: CS8604 at LoginHandler.cs:58 is a real defect — VerifyHashedPassword(.., null, ..) throws ArgumentNullException → 500 INTERNAL for magic-link-only accounts, which also tells them apart from unknown emails (no-leak rule of T-008-02). Fixing changes behaviour in a pure-refactor task and is security-shaped (AGENT.md §7).
OPTIONS:
  A) Null hash = failed verify (login_failed telemetry + LoginWrongPasswordException.ForWrongPassword()) + one LoginHandlerTests case — fixes the 500 and the leak; reuses the existing failure path.
  B) Keep behaviour: user.PasswordHash! with a comment naming a follow-up — zero behaviour change, defect stays.
RECOMMEND: A — the 500 is a bug and a no-leak violation; the fix is one condition on the existing wrong-password branch.
BLOCKS: CHORE-code-style-2 P2 (only the LoginHandler site + the TWAE flip)
```
```
DECISION: Should CorsAllowListMiddleware really send "Vary: Origin" (today the statement is a no-op)?
CONTEXT: CA1806 at CorsAllowListMiddleware.cs:40 — Headers.Vary.Append("Origin") is LINQ Enumerable.Append on StringValues, result discarded, so no Vary header is ever sent although the comment requires it (shared caches must not serve one origin's echoed response to another, TA-4.1.6). Fixing adds a response header (behaviour change, security-shaped).
OPTIONS:
  A) context.Response.Headers.Append(HeaderNames.Vary, "Origin") + a small middleware test — header sent as the comment intends.
  B) Delete the dead statement (today's effect is nothing) and open a follow-up — zero behaviour change.
RECOMMEND: A — the intent is documented and a missing Vary on an origin-echoing response is a cache-poisoning hazard.
BLOCKS: CHORE-code-style-2 P5 (only this site)
```

### Risks
- **SDK drift.** Local is SDK 11.0.100 and CI is `10.0.x`. The pinned AnalysisLevel removes the main gap. `dotnet format`, IDE analyzers or compiler warnings can still differ, so the PR CI is the arbiter. P5 step 3 gives the rule for an unexpected diagnostic.
- **Docker builds.** They see only `src/`. That is why all analyzer configuration lives in `src/.editorconfig` (§2.4). A CA setting added at the root later would break `docker build` under TWAE.
- **Integration coverage is CI-only** while Docker is down. These changes are only proven by tests/wa.api.integration: P2 CS0618, the P3 store and publisher ctors, the P4 middleware logging (NeutralLogSlotTests) and the whole P7 split. P1 makes CI run on the PR, so merge only on 9/9 green.
- **LoggerMessage adds an `EventId` property** to structured logs (§2.7). This is additive and has no PII. Saved App Insights queries filter on the message, not on EventId (TA-10.2). Record it in REPO-FACTS.
- **PII coverage.** The guard change (P4 step 1) lands before the conversion, and the selftest must prove it fails on the seeded shapes. Any [LoggerMessage] outside the private-static convention is a finding by design.
- **Hermes branches in flight.** A wide diff (330 files) conflicts with open `feature/*` branches. Phases are separate commits, so P8 (IDE0005) and P6 can be re-run mechanically after a rebase.
- **`verify_plan.py` coverage.** It ignores non-src/tests rows (`.github/workflows/ci.yml`, root `.editorconfig`). It flags every double-dash CLI flag as a CSS token, which is why this plan writes commands without such flags (e.g. `dotnet format` + `git status` instead of the verify flag). Plan-time result: CLEAN (this version of the script reports no drift for CREATE rows that do not exist yet).

### Open questions (not in scope; noted for follow-ups)
- Forward `ct` at the 5 CA2016 sites (a cancellation behaviour change).
- A CI `concurrency` group so superseded develop-PR runs are cancelled.
- Enforce using order or `var` later (a large mechanical diff).

### Docs close-out (orchestrator, `docs:` commit after merge-ready)
- `docs/AGENT.md`:
  - §3: new rule 11 "C# quality gates (CHORE-code-style-2)". It covers TWAE in src+tests; AnalysisLevel 10.0-recommended (src); EnforceCodeStyleInBuild with the root/src editorconfig split and why; TimeProvider instead of `DateTime.UtcNow`; `[LoggerMessage] private static partial` only (WaTelemetryReporter excepted); primary ctors with readonly fields; endpoints in src/wa.api/Endpoints/ and DI in `src/wa.api/ApiServices.cs`.
  - §4: add `dotnet format src/wa.slnx` (must leave the tree unchanged; CI `lint-dotnet`). Note that the `check-telemetry-pii.mjs` R7 now covers [LoggerMessage].
- `AGENTS.md` (Hermes rules, line 20 area): the same bullets in short form.
- `docs/REPO-FACTS.md`:
  - §2 header: the API surface lives in `src/wa.api/Endpoints/*.cs`, registered from Program.cs; route count unchanged.
  - Middleware-order line ref: the line ref `Program.cs:163–171` is already stale; point it at the new Program.cs lines.
  - Note: TimeProvider is registered in both roots; log events carry EventId.
- `docs/03-technical-architecture.md` TA-2.1 tree (line 133): Program.cs is now "host wiring + middleware order + Map*Endpoints calls". Add `ApiServices.cs`. Fix TA-2.5's `global.json` quote (`10.0.100` → `10.0.0`), only if the orchestrator wants that drive-by.
- `docs/Open-Decisions-and-Constants.md` Part 3: log the DECISION-1/2 answers plus the decided-alone items (AnalysisLevel pin, CA off-list, TWAE NU exception, LoggerMessage EventId).
- `.claude/skills/protodrop-cycle/references/pitfalls.md`: add "TWAE: any warning breaks build/CI. The analyzer config lives in `src/.editorconfig` (Docker sees only src/). The local SDK 11 vs CI SDK 10 gap. [LoggerMessage] must be private static partial or the PII guard flags it."
- `docs/PROGRESS.md` §1: one line for CHORE-code-style-2.
