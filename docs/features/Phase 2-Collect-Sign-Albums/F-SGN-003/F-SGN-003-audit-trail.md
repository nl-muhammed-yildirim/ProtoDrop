# F-SGN-003 — Audit Trail

**Priority:** P1 (Phase 2b) | **Phase:** 2b — Sign
**Spec source:** `02-feature-plan.md` F-SGN-003 (outline → remapped below as FR-032-*) | **Architecture:** TA-3.2, TA-9.4
**Milestone tasks:** T-050 (M5)

---

## Description

The trust layer: an **immutable log** of what happened to the document — viewed, signed, IP, timestamp — and the product **says so in plain language** (GDPR: "we log your view and signature with your IP hash and a timestamp; we keep no raw IP"). "Immutable" is honest in MVP: `AppendOnly` (no UPDATE path in the API; a DBA-level delete is the escape hatch, documented — true hash-chaining is a Phase 3 decision, D-22).

**Actors:** sender (reads the trail), signer (consented), operator (data-lifetime audit), regulator (the plain-language statement).
**Value:** the "audit trail" is the word that makes e-sign defensible; without a visible, stated trail, the product is a form, not a signature.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-032-1 | `AuditEntry` (TA-3.2 addition, migration): `Id (BIGINT identity), DocumentId, Type (Viewed|Signed|Declined|Voided), ActorEmail?, IpHash VARCHAR(64), UserAgentFamily VARCHAR(64), AtUtc, MetaJson?`. Append-only: no UPDATE/DELETE endpoint in the API. |
| FR-032-2 | Logged events: **viewed** (signer opens the sign page — once per (recipient, document), dedup 24 h window), **signed** (F-SGN-002), **declined**, **voided** (sender action, actor = sender). |
| FR-032-3 | IP: `HMAC-SHA256(ip, Key Vault key)` — **no raw IP persisted** (TA-9.4, same pattern as F-PRF-003). UA: family only. |
| FR-032-4 | The plain-language statement (shown on the sender's tracking view + in the sign email, link target): "We log each view and signature with a hashed IP and a timestamp. No raw IP is kept." (i18n string, F-TRF-014). |
| FR-032-5 | View: sender's tracking view shows a per-document timeline (type, actor email, time) — **IP hash not shown** in MVP (it's for the export/erasure path; showing a hash to a non-expert is noise, documented). |
| FR-032-6 | Lifetime: audit rows live with the document (deleted when the document's blobs are GC'd — F-SGN-004 keeps the final PDF; audit deleted at `DOCUMENT_RETENTION_DAYS`, proposed 90, pending D-22). |

## Acceptance criteria

```gherkin
AC-032-1: A signer opens the sign page
  Then a Viewed audit entry is written (once per 24 h)

AC-032-2: The signer signs
  Then a Signed audit entry is written in the same transaction as the Signature row

AC-032-3: The sender views the audit timeline
  Then each entry shows type, actor, and time — no raw IP, no hash

AC-032-4: The audit table is queried for a voided document
  Then the Voided entry shows the sender as actor

AC-032-5: Two view events 1 minute apart (same recipient)
  Then only one Viewed entry exists (24 h dedup)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-032-1 | Viewed without IP (privacy mode / no geo) | `IpHash = NULL` (allowed column), entry still written |
| EC-032-2 | Actor email unknown (declined by guest?) | Declined/signed always have an email (recipient row); viewed by a direct-link open without email → `ActorEmail = NULL` |
| EC-032-3 | Document GC'd while audit is being exported | Export reads before GC (documented race, acceptable at 90 d) |
| EC-032-4 | GDPR erasure of a signer | Erasure = delete their audit rows (they're the data subject); the hash makes it findable — the (recipientId, type) pair is the find key (documented) |

## UI notes (UI-Reference §5)

- Tracking view: timeline list (icon per type, actor email, time, `--fs-small`), collapsible; the plain-language statement as a footer note (`--fs-tiny` `--fg-muted`).
- No raw IP anywhere in the UI (AC-032-3).

## Technical notes

- `AuditEntry` DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.AuditEntry (
      Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEntry PRIMARY KEY,
      DocumentId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Audit_Doc REFERENCES dbo.Document(Id),
      Type          TINYINT NOT NULL,  -- 0 Viewed, 1 Signed, 2 Declined, 3 Voided
      ActorEmail    VARCHAR(320) NULL,
      IpHash        VARCHAR(64) NULL,
      UserAgentFamily VARCHAR(64) NULL,
      AtUtc         DATETIME2 NOT NULL,
      MetaJson      NVARCHAR(MAX) NULL
  );
  CREATE INDEX IX_Audit_Doc ON dbo.AuditEntry (DocumentId, AtUtc);
  ```
- View dedup: `WHERE NOT EXISTS (… Viewed for same recipient within 24 h)` in the write path (atomic with the page render, TA-7.2 pattern).
- Events (TA-5.3 extension): audit writes are **synchronous** (same transaction as the state change) — not via Service Bus (audit must not lag the sign).
- Telemetry (TA-10.2 extension): none new (the audit table *is* the record; `document_viewed` telemetry event optional, PII-safe).

## Test plan

- Unit: 24 h dedup predicate; IP-hash NULL handling; type enum.
- Integration: AC-032-1…032-5 (view-once, sign-atomic-with-transaction, void actor, no raw IP in responses).
- E2E: sign flow → timeline shows Viewed then Signed in order.

## User stories

| ID | Story | File |
|---|---|---|
| US-032-01 | Prove who signed and when | `US-032-01-proof-of-signing.md` |
| US-032-02 | Trust that the log can't be quietly edited | `US-032-02-immutable-log.md` |
| US-032-03 | Know exactly what data we keep | `US-032-03-stated-gdpr.md` |
