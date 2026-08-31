# US-032-01 — Prove who signed and when

**Feature:** F-SGN-003 — Audit Trail | **Status:** pending

---

**Story:** As a sender, I want a per-document timeline of views and signatures — who, what, when — so that "did they actually sign, and when?" is answered from the record, not from inboxes.
**Actor:** sender (reads the trail), signer (consented), operator.
**Goal:** the audit timeline in the tracking view — type, actor, time — for `Viewed`, `Signed`, `Declined`, `Voided` (FR-032-2, FR-032-5).

## Preconditions

- A document exists; at least one state change happened (a view, a sign, a decline, a void).

## Happy path

1. The sender opens `{origin}/sign/{linkId}`: under the signer rows, a collapsible **Activity** timeline — each entry: icon per type, actor email, localized time.
2. A signer opening their sign page writes a `Viewed` entry (once per 24 h per recipient, FR-032-2 / AC-032-5).
3. A sign writes `Signed` **in the same transaction** as the `Signature` row (FR-032-2 / AC-032-2) — the timeline and the signature can't disagree.

## Alternative flows

- **Voided document**: the `Voided` entry shows the sender as actor (AC-032-4) — "voided by {sender}" is the last line in the timeline.
- **No raw IP anywhere**: the timeline shows type/actor/time only — the hash is for the export/erasure path, not the UI (FR-032-3/5, AC-032-3).

## Acceptance criteria

```gherkin
Given a signer opened the sign page and then signed
When the sender opens the activity timeline
Then Viewed appears before Signed, each with actor and time, and no IP at all

Given the sender voids the document
When the timeline is viewed
Then the Voided entry shows the sender as actor
```

## Edge cases

- View without IP (privacy mode): `IpHash = NULL`, entry still written (EC-032-1).
- Viewed by a direct-link open with no email known: `ActorEmail = NULL` — the row renders "anonymous view" (EC-032-2).

## UI notes

- Timeline list: icon per type, actor email, time, `--fs-small`; collapsible; the plain-language statement (US-032-03) as a footer note (`--fs-tiny`, `--fg-muted`).
- No raw IP, no hash — ever, in the UI (AC-032-3).

## Technical notes

- `AuditEntry` (F-SGN-003 DDL): `Type (0 Viewed, 1 Signed, 2 Declined, 3 Voided)`, `ActorEmail?`, `IpHash?` (HMAC-SHA256 of the IP, Key Vault key — no raw IP persisted, TA-9.4), `UserAgentFamily`, `AtUtc`.
- Writes are **synchronous** (same transaction as the state change) — not via Service Bus: the audit must not lag the sign.
- View dedup: `WHERE NOT EXISTS (… Viewed for same recipient within 24 h)` — atomic with the page render (AC-032-5).

## Links

- Feature: `SGN-003-audit-trail.md` (FR-032-1, FR-032-2, FR-032-5, AC-032-1/2/4/5)
- Related: US-031-01 (sign writes the entry), US-030-04 (void writes the entry), US-032-02 (immutability)
