# Review: F-FND-005 — LimitsRecord, Ports/LimitsStores.cs, Persistence/LimitsStores.cs

## Findings

### 1. [nit] `src/wa.domain/LimitsRecord.cs` (EOF) — missing trailing newline at end of file
The diff states "No trailing newline at EOF." Every sibling in the repo ends with a newline (`Plan.cs`, `Ports/LimitsStores.cs`, `Persistence/WaDbContext.cs`, and test files all verified to terminate `\n`). This is an established repo convention being violated; it will show up as a spurious diff on any future touch of this file.

### 2. [nit] `src/wa.application/Ports/LimitsStores.cs:9` — user-supplied plan code interpolated into exception message
`base($"No seeded plan with code '{planCode}'.")` embeds caller input (the raw request's plan code) in the `Message`. The doc comment correctly says T-056 owns the Problem+JSON body shape and this is "not a … field," but if any current or future catch-all maps `.Exception.Message` into a response, an unbounded/HTML-injectable string from the client rides along. Cheap hardening: quote-escape or length-cap in the message, or keep the raw value only on `PlanCode`. Low severity because T-056 explicitly owns that mapping — flagging so it isn't lost when T-056 lands.

### 3. [verified] Domain constraint (T-053-01 exit)
`wa.domain.csproj` contains zero `<PackageReference>`/`<ProjectReference>` entries; the only non-BCL surface in `LimitsRecord.cs` is `[JsonPropertyName]` from `System.Text.Json.Serialization`, which ships with net10.0 (STJ). No snuck-in references.

### 4. [verified] JSON contract 1:1 (T-053-01 exit)
All 9 property names map to the exact camelCase keys in all three seed sources, and both test constants AND the T-052-04 seeds match byte-for-byte across `LimitsProviderTests.cs` (`FreeLimitsJson/ProLimitsJson/BusinessLimitsJson`), migration snapshot `WaDbContextModelSnapshot.cs`, initial migration `SeedPlansAndFeatureFlags.cs:82-84`, and `PlanConfiguration.HasData`. Types are also correct against the seeded values:
- `long`: maxTransferSize (5368709120 > int.MaxValue ✓), maxSingleFile, maxZipSize (9223372036854775807 = long.MaxValue — must be `long`, is `long` ✓), storageQuota.
- `int`: retentionDays/graceDays/maxDownloads (-1)/maxEmails/activeTransfersMax (-1) all within int range ✓.
No missing, extra, or mistyped fields; key ordering in the seeds matches property order (not required by STJ but consistent).

### 5. [verified] Port placement & single-adapter rule (T-053-02 exit)
Ports live in `wa.application.Ports`; adapters in `wa.infrastructure.Persistence`. A grep of all of `src/` for `IPlansStore|IFlagsStore` finds exactly: the port file, the adapter file, and `Limits/Caches.cs` (cache wrappers — out-of-slice but consistent with TA-0.2 rule 7's "only place … reads the DB" claim). No third implementation; no other direct DB read of these types.

### 6. [verified] AC-053-4: unknown plan → typed exception
Port contract (`IPlansStore.GetAsync` doc) and adapter behavior agree: `FirstOrDefaultAsync` on null → `throw new PlanUnknownException(planCode)` — never null, never an empty record. Exception is sealed with a read-only `PlanCode { get; }` (get-only auto-property assigned in ctor body via expression-bodied constructor — valid C#, no warning).

### 7. [verified] Adapter correctness & EF convention
- Filter column: query filters on `p.Code`; `Plan.cs` declares `public string Code`, and `PlanConfiguration` maps it to a unique index (`UQ_Plan_Code`) — the lookup hits an indexed, equality-only predicate (no N+1 risk; single row by design).
- EF usage matches sibling convention: `WaDbContext` exposes no `DbSet<>` properties (model is built purely via assembly-discovered configurations), so `_db.Set<T>()` in both adapters is exactly how this codebase reads entities — consistent with the rest of the folder. Cancellation tokens threaded through; async/await used properly (no `.Result`, no sync-over-async).
- `FlagsStore.GetAllAsync`: unfiltered `ToListAsync(ct)` returning covariantly as `IReadOnlyList<FeatureFlag>` matches the port contract ("all rows, unfiltered") and the T-053-04 layering plan.

### 8. [verified] Exception design baseline
`PlanUnknownException : Exception` is the **first** exception type anywhere in `src/` (grep for `: Exception` across all projects finds only this one). There is no pre-existing base class or pattern to be inconsistent with, so plain `Exception` + sealed subclass is acceptable; when a second domain exception appears, consider whether they should share an application-layer marker interface.

## Verdict
**approve-with-nits**

The slice satisfies every assigned exit criterion: the domain project stays BCL-only, all 9 camelCase keys match the frozen Part-2 seeds byte-for-byte across tests and both migration sources with correct `long`/`int` typing (including the long.MaxValue zip sentinel), ports/adapters are correctly placed with a single indexed EF adapter pair consistent with folder conventions, and AC-053-4 is enforced at both contract level and runtime. The only defects are cosmetic: missing trailing newline in `LimitsRecord.cs`, and an unescaped user-supplied plan code in the exception message that T-056 will need to handle when it owns response mapping — neither blocks merge.
