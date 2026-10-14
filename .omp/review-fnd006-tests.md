# Review — F-FND-006 tests (T-054-06/07, 7/7 math, fakes-only, docs)

## Findings

[verified] **Fakes-only in unit/integration tests**
- `tests/wa.domain.unit/Events/EnvelopeShapeTests.cs` — pure domain: only `JsonSerializer`, `EventEnvelope`, and the TA-5.3 list; no broker client, no fake publisher, no Testcontainers (whole file read).
- `tests/wa.api.integration/Events/InMemoryFakeRoundTripTests.cs` — constructs `InMemoryEventPublisher` directly with an injectable clock (`TimeSpan.FromMinutes(15), () => now`, line 36); zero infrastructure imports.
- `OutboxRetryTests.cs` uses Testcontainers SQL 2022 (legitimate: real DB required for the PK-dedup proof) and constructs the *real* `SbEventPublisher` with an empty configuration so the broker ctor throws inside the adapter's try — no live Service Bus anywhere in T-054-06/07.

[verified] **7/7 math**
- EnvelopeShapeTests: exactly 2 `[Fact]` (lines 21, 49). OutboxRetryTests: exactly 2 `[Fact]` (lines 38, 65). InMemoryFakeRoundTripTests: exactly 3 `[Fact]` (lines 16, 33, 50). Total = 7; no extra test methods or Theories in those files.
- `EventDeduplicatorTests.cs`: 4 tests, all T-054-04 (AC-054-3), in wa.application.unit — correctly outside the 7/7 count (consistent with application.unit 22/22 including them).

[verified] **TA-5.2 byte-exact**
- `ExpectedEnvelope` template (EnvelopeShapeTests.cs:61–64) matches the shared-contract reference string exactly: same GUIDs (`1111…`, `6666…`), `"version":1`, ISO-8601 Z timestamp derived from a fixed UTC DateTime, key order pinned by both full-string equality (per type) and an independent JsonDocument key-sequence check (line 56–58: exactly the seven fields in TA-5.2 order). Per-type loop derives expected strings from one template with only `eventType` differing — no drift possible between types.
- InMemoryFakeRoundTripTests.cs:59–61 pins the full literal string byte-exact on read-back; OutboxRetryTests.cs:81 asserts stored outbox row JSON equals a fresh serialization (storage fidelity, shape pinned elsewhere).

[verified] **Closed TA-5.3 list pinned**
- All 11 wire names present in `ta53Types` and match the closed list exactly (transfer.created … admin.action); `Assert.Equal(11, ta53Types.Length)` plus order-sensitive `Assert.Equal(ta53Types.Select(...), Enum.GetValues<EventType>())` pins both membership and enum declaration order.

[verified] **Retry idempotency proof is real**
- DB-level PK: migration + config (`DomainEventsConfiguration.cs`, snapshot) set `PK_DomainEvents` on EventId — duplicate eventId impossible at the table level, not test-side dedup.
- Failure path row committed *before* publish attempt: `SbEventPublisher.PublishAsync` saves (line 56) before entering the broker try; empty config → `ServiceBusClient` ctor throws inside the catch, so the first publish leaves a committed `IsPublished=false` row with byte-exact `EnvelopeJson`, asserted via a separate `_db` context.
- Second publish of same eventId goes through `AppDbContextFactory.CreateDbContextAsync` (fresh context per call — no change-tracker shadowing), so the INSERT hits the real PK and throws `DbUpdateException`; final count 1 is queried on an independent context → DB-level rejection proven.

[verified] **No tautology tests**
- No bare not-throw or non-empty-only assertions: every Fact asserts stored shape/state (exact JSON, counts, order, TTL state). TTL via injectable fake clock with deterministic boundary (`now - StoredAtUtc >= ttl` at exactly +15 min — no real waits); closure over the reassigned `now` local is valid C# and advances correctly.

[verified] **Docs consistency**
- US-054-03-in-memory-fake.md:50 reads "unit tests 7/7 (new tests across T-054-06 + T-054-07) … full §4 gate green (domain.unit 3/3, application.unit 22/22, api.integration 9/9)" — counts match the verified head and use the "7/7" phrasing.
- F-FND-006-event-backbone.md:89–95 — all seven rows T-054-01…T-054-07 ☑; line 69 exit-evidence line present with matching 7/7 wording and gate statement.
- Milestone-Backlog.md:19 — T-006 status `done`.

[nit] **Weak shape assertion in first round-trip test** (InMemoryFakeRoundTripTests.cs:25–26)
- Uses substring `Assert.Contains("\"eventType\":\"transfer.created\"")` rather than full-string equality. Not a defect — byte-exact literals are pinned in the third Fact of this file and by EnvelopeShapeTests, so coverage is complete; but if the envelope ever gained an *extra* field after these keys, only those tests would catch it (they do). No action required at M0.

## Verdict
PASS — 7/7 math exact, TA-5.2 shape pinned byte-exact for all 11 closed types, PK-dedup proof is DB-level with the failure row committed before publish, fakes-only constraint holds in every T-054-06/07 file, and docs (US line 50 counts + "7/7" phrasing, feature rows ☑, backlog done) are consistent.
