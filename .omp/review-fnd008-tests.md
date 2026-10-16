# Review — F-FND-008 tests (T-056-01/04/05: W3CTraceParserTests, TelemetryEventsTests, WaTelemetryReporterTests, PipelineTests)

## Findings

[verified] **W3CTraceParserTests: 24 test cases — strict W3C v00 coverage (AC-056-1)**
- `tests/wa.domain.unit/Correlation/W3CTraceParserTests.cs` — 97 ln, read fully. 11 [Fact]/[Theory] markers = 9 facts + 2 theories (one with 3 InlineData rows for absent/blank header values, one with 12 InlineData rows for malformed variants).
- Malformed theory covers: wrong version (`01-`, `zz-`, `A0-`), trace-id length 31/33 chars, parent-id length 15/17 chars, only-three-parts (missing flags), five-parts (extra dash in flags), non-hex char in trace-id, uppercase hex in trace-id, empty trace-id segment (double dash). All-zero trace-id is a separate [Fact] (line 4 of the test file — `Parse_AllZeroTraceId_IsInvalid`).
- No bare not-throw or non-empty-only assertions anywhere; every case pins an exact value. `NewTraceId` shape and cross-call randomness tested separately from the parse logic.

[verified] **TelemetryEventsTests: 10 test cases — closed-set equality + PII transform vectors (AC-056-4)**
- `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` — 87 ln, read fully. 8 [Fact]/[Theory] markers = 6 facts + 2 theories (each with 2 InlineData rows). Total xunit invocations: 1+1+2+2+1+1+1+1 = 10.
- `AllNames_IsExactlyTheTaDotTenPointTwoClosedSet_NoAdHocMembers`: HashSet equality against the literal TA-10.2 list (21 events + 7 metrics verbatim from spec lines 816/818) — any ad-hoc member or rename shows up as a diff, not just a missing one.
- `ConstMembers_AreExactlyTwentyEventsPlusSevenMetrics`: reflection count via `GetRawConstantValue()` on all public static literal fields = exactly 28; catches computed members that could slip in without changing the name list.
- PII test vectors pinned: SHA-256("a.txt") = `18b7cb09…b993`; HMAC-SHA-256(key=Wa:Jwt:Secret test key, "10.0.0.1") = `0ee7d127…faea` — same key string as WaTelemetryReporterTests so both projects share one known vector (TA-9.4).

[verified] **WaTelemetryReporterTests: 8 test cases — template shape + closed-set gate (AC-056-4)**
- `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` — 109 ln, read fully. 7 [Fact]/[Theory] markers = 6 facts + 1 theory with 2 InlineData rows → 8 total invocations.
- `LogEvent_EmitsExactlyTheClosedSetNameAsTheWholeTemplate_NoOtherProperties`: asserts the rendered text IS exactly `"upload_started"` and that Args is empty — no properties beyond log context (TA-10.2 line 816: upload_started has zero contract-named properties).
- `UploadCompleted_UsesTheOnlyContractNamedProperties_BytesDurationMsRetries`: pins both exact template text (`"upload_completed 123456 bytes in 789 ms after 2 retries"`) AND argument order `(bytes, durationMs, retries)` — T-056-05's PipelineTests test 3 re-asserts this through real Serilog output.
- `LogEvent_AdHocName_ThrowsBeforeAnythingIsEmitted`: asserts nothing half-emitted (EC-056-4: fail fast at the gate, not silently in App Insights).
- RecordingLogger (lines 971–992): custom ILogger<WaTelemetryReporter> that records RenderedText + pre-formatted args via ExtractArgs (FormattedLogValues enumeration preserving argument order) — no tautology: it doesn't call any reporter method, only the M.E.L extension overloads.

[verified] **PipelineTests: 3 integration tests — AC-056-1/2/4 end-to-end**
- `tests/wa.api.integration/PipelineTests.cs` — 118 ln, read fully. ThrowingRouteFactory uses IStartupFilter (lines 38–46) whose Map() runs AFTER next(app) — inside Program's own Correlation/GlobalError scope; this is the only test-only injection point that exercises the full middleware chain including both middlewares' positions relative to routing.
- Test 1 (`UnknownRoute_Returns404ProblemJson_BodyCorrelationIdMatchesLogContextValue`): GET /no-such-route-m0 → asserts code NOT_FOUND, status 404 exact; type/title/details presence via TryGetProperty (presence checks — ProblemWriter.Build is the single construction site so drift risk is low); correlationId regex `^[0-9a-f]{8}$`; traceparent response header's trace-id starts with that same correlationId (TA-10.1 "traceparent in → out" + FromLogContext enrichment).
- Test 2 (`ForcedUnhandledException_Returns500ProblemJson_NoStackTraceInBody`): GET /pipeline-test/throw → asserts code INTERNAL, status 500; `DoesNotContain("at ", body)` — no C# stack frames leak; exception message not leaked either (line 90: `DoesNotContain("T-056-05 forced failure", body)`).
- Test 3 (`UploadStartedStyleEvent_ThroughWaTelemetryReporter_ProducesTheExactTa102TemplateNameAndNoOtherProperties`): real Serilog pipeline via in-memory RecordingSink (lines 112–117); asserts template text IS exactly `"upload_started"` and Properties has only one key: `SourceContext`. Proves the M.E.L bridge enriches with SourceContext but no other properties — TA-10.5 holds end-to-end.

[verified] **Gate counts match the diff**
- domain.unit: 37/37 = baseline 3 (T-054) + W3CTraceParserTests 24 + TelemetryEventsTests 10 → 3+24+10=37 ✓
- application.unit: 33/33 = baseline 25 (T-055) + WaTelemetryReporterTests 8 → 25+8=33 ✓
- api.integration: 13/13 = baseline 10 (T-055) + PipelineTests 3 → 10+3=13 ✓

[minor] **PipelineTests test 1 asserts field presence, not exact key-set equality**
- Test 1 uses `TryGetProperty` for type/title/details — a future addition of an extra top-level property would still pass. ProblemWriter.Build is the single construction site (T-056-02), so drift risk is low; optionally pin `Assert.Equal(7, root.EnumerateObject().Count())` in a T-056-fix commit to lock the exact key set.

- **Fixed in `T-056-fix:` (9491dc8):** test 1 now pins the exact key set AND order via `Assert.Equal(["type","title","status","code","details","correlationId"], …)` — stronger than the suggested count pin, and catches both drift directions.
[verified] **Docs consistency (commit 9be0945)**
- All 5 task docs under F-FND-008: every `- [ ]` exit checkbox flipped to `- [x]`; F-FND-008-pipeline-foundation.md milestone rows lines 92–96 all ☑; Milestone-Backlog.md line 21 T-008 pending → done.

## Verdict
PASS — all four test files read fully and verified against spec; no tautologies (RecordingLogger/RecordingSink don't call production methods); AC-056-1/2/4 proven end-to-end through real Kestrel/TestServer + Serilog pipeline; gate counts 37/33/13 confirmed by arithmetic over baseline. One `[minor]` finding: test 1's field-set check was presence-only — fixed in `T-056-fix:` (9491dc8) with exact key set + order pinned.
