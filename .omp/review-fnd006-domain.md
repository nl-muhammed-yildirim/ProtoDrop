# Review — F-FND-006 T-054 slice: domain + port + migration

## Findings

[nit] `src/wa.infrastructure/Persistence/WaDbContext.cs:28` — comment says "15 configurations in EntityConfigurations/ (one per entity)" but the folder now holds 16 (`DomainEventsConfiguration.cs` added). Stale count introduced by this change; discovery still works via `ApplyConfigurationsFromAssembly`, so no functional impact.

[nit] `src/wa.domain/EventEnvelope.cs:85-112` — `EventTypeConverter` is redundant with the `[JsonPropertyName]` attributes already on each enum member (STJ honors those natively for enums since .NET 5, producing exactly these wire names under plain option-free serialization). Both mechanisms agree today; keeping both means a future explicit-value or reordered enum could silently desync from `(int)value` indexing. One of the two should be dropped, or an assertion pinning declaration order added.

[nit] `src/wa.application/Ports/Events.cs:6-24` — port doc comments describe implementation ("EF Core change tracker", "same transaction as business data") rather than contract; no EF reference in the file itself so layering holds, but a second non-EF publisher would have to match that wording or it goes stale. The 10 s timeout is correctly kept at adapter level (`SbEventPublisher.cs:24-25`), not pinned here — good.

[nit] `src/wa.domain/DomainEvent.cs:9` — doc typo "Ta-5.3" (capitalization; TA-5.3 everywhere else).

[verified] **TA-5.2 byte-exact shape** (`EventEnvelope.cs:51-80`): exactly 7 properties, declaration order `eventId, eventType, version, occurredAt, correlationId, partitionKey, payload` — matches the reference key order; every property carries a `[JsonPropertyName]`, no extras/missing on the POCO. The exact full-string assertion at `tests/wa.api.integration/Events/InMemoryFakeRoundTripTests.cs:59-61` pins it (count verified 9/9 at this head).

[verified] **TA-5.3 closed list** (`EventEnvelope.cs:10-45,87-112`): exactly the 11 wire names in spec order; `Read` throws `JsonException` on unknown wire string (line 93); `Write` emits via index built from declaration-order attributes — correct for this all-implicit enum.

[verified] **wa.domain BCL-only**: csproj has zero NuGet/project references (`wa.domain.csproj:1-9`). Both files use only implicit/`System.*` usings; no EF attributes anywhere in `EventEnvelope.cs` or `DomainEvent.cs`. Golden-rule #7 clean (no fake-detection branches).

[verified] **Port** (`Ports/Events.cs`): MediatR-free (only `using wa.domain`). Return type `DomainEventRecord { Guid EventId; bool IsPublished }` — exactly the contract's shape, separate from EF entity `wa.domain.DomainEvent`. Doc comments consistent with behavior: topic `core`, MessageId = EventId, unpublished-on-failure semantics (EC-054-1).

[verified] **Config ↔ migration parity** (`DomainEventsConfiguration.cs` vs `20261012164301_DomainEvents.cs`): no drift —
| Column | Config | Migration | Snapshot |
|---|---|---|---|
| EventId PK, named `PK_DomainEvents`, uniqueidentifier, NOT NULL | ✓ (line 15-16) | ✓ (lines 18,29) | ✓ (snapshot:180-213) |
| EventType VARCHAR(64), required | ✓ | nullable:false | IsRequired + type match |
| EnvelopeJson NVARCHAR(MAX), required | ✓ | nullable:false | IsRequired + type match |
| CorrelationId uniqueidentifier, NOT NULL (non-null Guid ⇒ required; config omits `.IsRequired()` but EF infers it) | — | nullable:false | no IsRequired flag on snapshot property is the normal representation for a non-nullable value-type column |
| PartitionKey VARCHAR(320), nullable (`string?`) | ✓ | nullable:true | type match, no IsRequired |
| IsPublished BIT, NOT NULL | ✓ (line 22) | nullable:false | type match |
| OccurredAtUtc / CreatedAtUtc datetime2 | omitted in config ⇒ EF SqlServer convention `datetime2` | both `datetime2`, NOT NULL | both `datetime2`; consistent — no other configuration file pins these either, so repo-wide pattern matches |

[verified] **Migration additive-only**: single `CreateTable("DomainEvents")` + PK constraint; `Down` drops the table. No index changes (re-publish scan needs none at M0). Snapshot section for `wa.domain.DomainEvent` (`WaDbContextModelSnapshot.cs:180-213`) matches the migration exactly — same columns, types, nullability, key name, no indexes.

[verified] **ADR row** (`docs/Open-Decisions-and-Constants.md:94`): present; cites correct migration id `20261012164301_DomainEvents`, table name and full column list match the DDL verbatim (incl. IsPublished BIT, datetime2 timestamps). Its claim that stale row line 87's migration `20260901170311` "does not exist in the current tree" is true — migrations folder contains only InitialCreate (20261011213139), SeedPlansAndFeatureFlags (20261011215315) and DomainEvents (20261012164301).

## Verdict
PASS — envelope POCO is byte-exact per TA-5.2, closed list + converter are correct, domain layer stays BCL-only with zero references, port matches the intended design decision, config/migration/snapshot/ADR all agree; only nits (stale comment count, redundant converter mechanism).
