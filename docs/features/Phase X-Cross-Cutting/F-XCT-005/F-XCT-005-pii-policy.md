# F-XCT-005 — PII Policy (emails, sender names, telemetry)

**Priority:** P1 (foundation — enforced from day one) | **Phase:** X — Cross-cutting
**Spec source:** `02-feature-plan.md` §6 (F-XCT-005) | **Architecture:** TA-9.4
**Milestone tasks:** T-069

---

## Description

The PII policy is the *standing law*: exactly which fields are PII, where raw PII may live, how it appears in telemetry, and what "erased" covers. It already exists in TA-9.4 in principle — this feature makes it **enforceable**: a typed `Pii` marker in code, a CI check that raw emails can't sneak into telemetry, one canonical list everyone cites, and the GDPR interlock (F-XCT-004) that makes deletion sweep what the policy says is PII.

**Actors:** developer (marks PII fields, obeys the lint), DPO (cites the list), App Insights (the sink where PII goes wrong).
**Value:** "is this field PII?" is answered by a table, not by memory; and a violation is caught in CI, not in an audit.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-048-1 | **Canonical PII list (TA-9.4, made normative):** PII = **email addresses, sender names, file names, IPs**. Derived forms are PII too when they identify a person: unsubscribes keyed by (address, sender), `EmailSuppression` rows, `SenderName` on re-homed transfers (US-008-06 keeps it deliberately — it's *anonymized-attribution*: name without address, and the policy says so). |
| FR-048-2 | **Raw PII locations (closed list):** (a) `AppUser.Email`, (b) `EmailRecipient.Address`, (c) `Transfer.SenderName`, (d) `TransferFile.FileName`, (e) `EmailSuppression` (address pair), (f) `DownloadEvent` IP (F-PRF-003), (g) `IdpConfig`/`OrgMember` emails in Phase 3. Raw PII lives **only** in these columns — the policy test asserts every other column of every table is PII-free by naming (the "no `Email`/`Name` in a surprise place" rule). |
| FR-048-3 | **In telemetry:** file names → **SHA-256**; IPs → **HMAC(`Wa:Jwt:Secret`)**; email addresses → raw only in a **PII-flagged property** (`Pii=true` custom property in App Insights, TA-9.4). No secrets (JWT secret, SAS token, Stripe key) in any event — a CI grep on event-helper call sites (the `translation_missing` lint precedent, TA-10.5) plus the `Pii=true` marker as the *only* place a raw value may sit. |
| FR-048-4 | **Code marker:** every PII field on a POCO carries `[Pii]` (or the `Pii=true` attribute in the event DTO); a source-generator or convention test fails the build when a `string` property named `*Email*`, `*Name*`, or `*Ip*` reaches an event payload or a log template **without** the marker (the "marked PII" rule from EC-012-3, now a gate, not a review convention). |
| FR-048-5 | **Log levels (TA-10.5, made a test):** default Information logs may contain only marker-cleared values; PII at Information is allowed **only** through the explicit PII slot (`{Message:Pii}` enrichment) and lands in App Insights as a PII-flagged property. A unit test asserts the enrichment mapping. |
| FR-048-6 | **Erasure interlock (F-XCT-004):** `DELETE /auth/me` erases exactly the FR-048-2 closed list rows + the telemetry sweep (US-047-02). Any *new* raw-PII column added to the schema must be added to the erasure cascade in the **same** migration (convention test: migration files that touch a `Pii`-marked column must reference the erasure command — enforced in code review via the marker, test via the cascade list). |
| FR-048-7 | **No PII in correlation ids:** `correlationId` (8-hex) is an id, not a name — safe at any log level (consistent with US-012-01). |
| FR-048-8 | **Phase 3 additions:** `OrgMember.Email`, `IdpConfig` group claims, and workspace names are PII per this list; the closed list in FR-048-2 already names them so Phase 3 migrations are checked against the same table. |

## Acceptance criteria

```gherkin
AC-048-1: An event payload contains a file name
  When the event is emitted
  Then the file name is SHA-256 in the payload
  And the raw name appears in no property (checked by the CI grep on event helpers)

AC-048-2: An email address must be recorded
  When an event or log carries a recipient address
  Then it sits in a Pii=true flagged property only
  And the default-level log template carries the marker, not the raw value

AC-048-3: A new column that is PII
  When a migration adds a Pii-marked column
  Then the build's convention test passes only if the column is in the raw-PII closed list and the erasure cascade
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-048-1 | `SenderName` on a re-homed transfer after owner deletion | Deliberate raw PII in a "anonymous" transfer — the policy documents it as name-without-address; the erasure report counts it (US-047-03) so it's visible, not hidden |
| EC-048-2 | A developer logs a full email at Information without the marker | Convention test fails the build (FR-048-5) — the fix is the marker or a masked value (`d****@x.com`) |
| EC-048-3 | HMAC key rotation (`Wa:Jwt:Secret`) | Old IPs are no longer reversible with the new key; the policy accepts loss-of-reversibility (events are counts, not identity lookups) — documented here, not in an ADR (policy, not architecture) |
| EC-048-4 | File name SHA-256 collisions across users | Accepted — the hash is for *anonymization*, not identity; two users hashing the same name share a value by design |
| EC-048-5 | A flag value that looks like PII (e.g., someone saves an email into a flag) | Flag values are `limits.*`/`feature.*` typed (FR-044-3); the Flags screen hints "numbers and booleans only" — the audit row's `DetailsJson` is PII-free by the same marker rule |

## UI notes

- No user-facing UI. The only surfaces this policy *changes*: (a) the delete-account modal's telemetry line (FR-047-7, F-XCT-004), (b) admin screens masking emails (`d****@x.com` — F-TRF-011, F-XCT-004), (c) CSV exports masking per the DPO rule (US-047-03).

## Technical notes

- `[Pii]` attribute on DTO/entity properties + `Pii=true` App Insights custom property (TA-9.4 already names both; this feature *enforces* them).
- Convention tests (wa.application.unit / wa.infrastructure): (1) event payload types — every PII-typed property must carry `[Pii]`; (2) log template scan for `*Email*`/`*Name*`/`*Ip*` without the marker; (3) raw-PII closed list — every `[Pii]` column must appear in the erasure cascade array (single source: `ErasureScope.AllColumns`, kept in `wa.domain` so the test and the command share it).
- Hashing helpers: `Sha256Hex(string)` for file names; `HmacIp(ip, key)` with `Wa:Jwt:Secret` — both already implied by TA-9.4; T-069 adds the unit tests that *name* them as the policy.
- The CI grep on event-helper call sites follows the `translation_missing` lint precedent (EC-012-3): a script in the pipeline, not a new analyzer package (no dependency rule violation, TA-17).

## Test plan

- Unit: hash/HMAC helpers (determinism, key-sensitivity); `[Pii]` marker convention tests on three fixture DTOs (marked/unmarked/surprise-column); erasure cascade list completeness against the `[Pii]`-marked entity columns.
- Integration: an emitted `email_sent` event — assert the payload has no raw address outside the PII property; a 500 error JSON — no PII fields (correlation id is fine).
- E2E: none (policy is invisible); verification = the erasures screen + App Insights PII-property queries (manual DPO script).
- Exit check (T-069): "Convention tests red on an unmarked email in a fixture; green after marking; erasure cascade covers every [Pii] column."

## User stories

| ID | Story | File |
|---|---|---|
| US-048-01 | My file name is anonymous in telemetry | `US-048-01-anonymous-telemetry.md` |
| US-048-02 | Catch a PII leak before release | `US-048-02-ci-gate.md` |
| US-048-03 | Cite the PII list (DPO) | `US-048-03-cite-policy.md` |
