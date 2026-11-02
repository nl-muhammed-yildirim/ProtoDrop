# T-012-02 — Browser telemetry sink: formalize growth CTA + emit upload_failed / download_completed (FR-012-1, AC-012-1)

**Story:** US-012-01 | **Spec:** FR-012-1 / AC-012-1 (block 2); TA-9.4/TA-9.6 "PII" | **Size:** M
**Depends on:** T-012-01 (client traceparent helper)

---

## Context to read (only these)

- `US-012-01-correlation.md` → happy path 5 + Technical notes (PII, the ADR pointer for the two M0 gaps)
- `../F-TRF-012-telemetry.md` → FR-012-1 + AC-012-1 block 2 (the client events must exist in dev App Insights)
- `src/wa.web/src/features/recipient/growthTelemetry.ts` → the M0 console.debug sink being formalized (ADR: Open-Decisions-and-Constants.md Part 3, T-003-07 row)
- `src/wa.application/Telemetry/WaTelemetryReporter.cs` + `src/wa.domain/Telemetry/TelemetryEvents.cs` → the closed-set contract the client must mirror (TA-10.2 "names are code")

## Instructions

1. Add a **client telemetry sink** in `src/wa.web/src/core/telemetry/`: one module, test-overridable (same convention as UploadEngine's records), that emits to dev App Insights via the lightweight JS SDK or `fetch` POST — pick whichever needs fewer runtime deps; pin + ADR row. It must carry the T-012-01 traceparent for the current action so client events join the server traces.
   - The M0 console.debug convention (growthTelemetry.ts) becomes the sink's **dev fallback**: no AI key configured → same `console.debug` output as today, tests stay green offline.
2. **Formalize `growth_cta_shown` / `growth_cta_clicked`** through the sink: add both to a client-side closed list (mirroring TelemetryEvents.cs), with the `source` dimension (`post-download` / `terminal-state`) exactly as T-003-07 emits it. Update TA-10.2's event list in `docs/03-technical-architecture.md` — this is the contract change the ADR promised ("TA-10.2 to be updated at F-TRF-012").
   - ASSUMPTION line: TA-9.4 says "file names → SHA-256; IP → HMAC" but does not specify a client-side slot — reuse the server's `PiiTransforms` semantics (SHA-256 file name) where the client carries one; no IP in client events at M0 (the browser can't know it reliably without an endpoint). Note both in the sink module comment.
3. **Emit the two missing upload/download events** from the existing pages: `upload_failed` (UploadEngine's final-failure path — FR-001-6 records carry the retry count, no new properties per TA-10.2) and `download_completed` (bytes + durationMs after the browser-leg stream finishes — this closes the T-003-06 ADR gap). Both via the sink; both names already in TelemetryEvents.cs/TA-10.2 (no contract change).

## Exit check

- [ ] AC-012-1 block 2: a browser upload failure + completed download + both growth CTA events appear in dev App Insights within ~30 s, each carrying the action's T-012-01 traceparent
- [ ] `growthTelemetry.ts`'s M0 console output no longer bypasses the sink (it calls it — or is deleted with callers migrated)
- [ ] TA-10.2 in 03-technical-architecture.md lists `growth_cta_shown` / `growth_cta_clicked`; closed list fails fast on any other name (EC: ad-hoc names throw/are dropped, not silently logged)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (React 19 + Vite; .NET API emits via WaTelemetryReporter).
Task T-012-02 — the browser telemetry sink (milestone task T-025, scope: "the browser's upload/download events appear in App Insights with the right correlationId").
Read first (only): US-012-01-correlation.md (happy path 5 + Technical notes), F-TRF-012-telemetry.md (FR-012-1/AC-012-1 block 2), growthTelemetry.ts (M0 sink being formalized), WaTelemetryReporter.cs + TelemetryEvents.cs (closed-set contract).
Do exactly:
1. Sink module in src/wa.web/src/core/telemetry/: test-overridable, carries the current action's traceparent, console.debug fallback when no AI key (M0 convention — tests offline-green).
2. Formalize growth_cta_shown/growth_cta_clicked with source dimension through the sink; add both to TA-10.2's event list in docs/03-technical-architecture.md + client closed list (fail fast on ad-hoc names).
3. Emit upload_failed (UploadEngine final failure) and download_completed (bytes, durationMs — closes the T-003-06 ADR gap) from their existing pages.
Done when: AC-012-1 holds in dev — a failed upload, a completed download, and both growth CTA events each land in App Insights within ~30 s with the action's traceparent, and TA-10.2 is updated.
Constraints: PII per TA-9.4 (client-side slots assumed — see Instructions); no new endpoint; keep M0 console fallback so vitest runs without network; ADR row for the SDK choice in Open-Decisions-and-Constants.md Part 3.
```
