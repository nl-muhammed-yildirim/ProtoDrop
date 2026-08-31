# F-XCT-003 — Email Settings (Notification Preferences)

**Priority:** P1 | **Phase:** X — Cross-cutting
**Spec source:** `02-feature-plan.md` §6 (F-XCT-003) | **Architecture:** TA-9.4, TA-4.2
**Gate:** `feature.emailSettings` (F-XCT-001) | **Milestone tasks:** T-067

---

## Description

Every notification email (F-TRF-006) and later billing email (F-BIL-002) lands in the same inboxes — and some users want *none* of them. F-XCT-003 gives signed-in accounts a **Notification preferences** section in Profile: one switch, "Email me about my transfers and accounts," backed by one nullable column. Digest mode (grouping several notifications into one email) is explicitly **P2** and left out of this feature's schema impact.

**Actors:** signed-in user (toggling their own setting), operator (reading the suppression in admin), `f-email` worker (checking the preference at send time).
**Value:** one setting stops the #1 email complaint ("why am I getting these?") without deleting the account — and without confusing per-sender unsubscriptions (F-TRF-006), which stay independent.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-046-1 | `AppUser` gains `EmailNotificationsEnabled BIT NULL` — **NULL = on** (default for existing and new rows). Explicit off = `0`. The three-state encoding keeps the column nullable so "never touched" is distinguishable from "turned off" in the data. |
| FR-046-2 | Profile API: `PATCH /api/v1/auth/me` (endpoint 13) accepts `{ "emailNotifications": true|false }` alongside the existing name/theme fields; the field is additive — requests without it leave the value unchanged. `GET /api/v1/auth/me` returns `emailNotifications` (effective: NULL→true). |
| FR-046-3 | **Check at send time:** `f-email` resolves the owner of the transfer's notifications (`Transfer.OwnerAppUserId`) and skips the send when the setting is off. Skipped sends emit **no** `email_sent` (nothing was sent) and **no** other event — to keep the TA-10.2 closed list intact, suppressed sends log at Information level with `suppressedReason=email_setting` and emit no event (names are the contract; no new event). |
| FR-046-4 | **Scope of suppression:** only **notification** emails — transfer notifications to the *owner* (my transfer expired, download limit approaching, my re-send sent). Recipient notification emails (sent to *other* people) are governed by their own unsubscriptions (F-TRF-006 `EmailSuppression`), not by the sender's setting. Billing emails (F-BIL-002) are **not** suppressed — money emails must arrive (documented on the settings screen). |
| FR-046-5 | **Guests:** a transfer without `OwnerAppUserId` has no setting — everything sends (status quo). Suppression never applies to guests. |
| FR-046-6 | **Gate:** the setting is only editable and only effective when `feature.emailSettings` is true (F-XCT-001). Flag off → Profile hides the toggle; the worker ignores the column (all sends happen). Absent flag key = off (fails closed for the *feature*, i.e., the column is inert). |
| FR-046-7 | **GDPR interplay:** account deletion (F-TRF-008-8) removes the row with the setting — no separate erasure step. The setting is PII-adjacent (a preference, not an identity): stored on `AppUser`, erased with it. |
| FR-046-8 | **Digest (P2, out of scope):** no schema for digests is added here; the P2 feature will add `EmailDigestEnabled` and a grouping window. This feature must not block that (the nullable-bit pattern is deliberately extendable). |

## Acceptance criteria

```gherkin
AC-046-1: A user turns notifications off
  Given I am signed in and my setting is on
  When I set emailNotifications to false in Profile
  Then my account's notification emails are skipped from the next send on
  And my recipients still get their emails

AC-046-2: Suppression reaches the worker
  Given a transfer owner has the setting off and a suppression-relevant event fires (e.g., expiry warning)
  When f-email processes it
  Then no email is sent and no email_sent event is emitted for that send
  And the skip is logged at Information with suppressedReason=email_setting

AC-046-3: Billing is exempt
  Given my setting is off
  When a Pro renewal invoice is issued (F-BIL-002)
  Then the billing email is sent
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-046-1 | Setting changed while an email is already in the outbox/queue | At most one email in flight is affected; the next send observes the new value (send-time read, no precomputed list) |
| EC-046-2 | User toggles off, then deletes their account, then re-registers | New row → NULL → on (documented: preferences don't survive deletion) |
| EC-046-3 | Recipient of a *recipient* email complains the sender "should have suppressed me" | Different mechanism: per-(address, sender) unsubscription (F-TRF-006); documented on the settings screen ("This does not affect emails sent to your recipients") |
| EC-046-4 | `feature.emailSettings` off in an environment | Column may be edited only via API in tests; UI hidden; worker ignores it — a row with `0` behaves like `NULL` in that environment |
| EC-046-5 | Race: two `PATCH /auth/me` calls (theme + setting) | Last write wins per field (PATCH semantics, endpoint 13); no row lock beyond the transaction |

## UI notes

- Profile → **Notification preferences** (UI-Reference §5.5 pattern): one switch "Email me about my transfers and accounts," helper text: "Recipients always get their download emails. Billing emails always arrive."
- Saved immediately on toggle (no save button — single-value form); toast "Notification preferences saved."
- Hidden when `feature.emailSettings` is off (US-044-02 contract: absent, not disabled).

## Technical notes

- Migration: `ALTER TABLE AppUser ADD EmailNotificationsEnabled BIT NULL` — T-067, same EF Core migration pattern (TA-2.3).
- `f-email` read path: the event payload already carries `ownerAppUserId` (TA-5.2 envelope payloads are unchanged); the worker joins `AppUser` for the setting *at send time* (not at publish time), so a late toggle can still catch a queued send (EC-046-1 window = one message).
- `PATCH /auth/me` handler: additive field merge (existing behavior for name/theme — extend, don't fork).
- Telemetry: none new (FR-046-3); the Information log line is grep-able by `suppressedReason=email_setting`.

## Test plan

- Unit: effective-value resolution (NULL→true, 0→false, 1→true); PATCH additive merge (setting unchanged when field absent).
- Integration: `f-email` fake-CommunicationHub — owner off → no send, no `email_sent`; owner on → send; recipient emails unaffected by the sender's setting; billing email sent while off (AC-046-3).
- E2E: Playwright — toggle off in Profile, trigger an owner notification (admin-forced event or expiry in dev), assert inbox fake empty.
- Exit check (T-067): "Toggle off stops owner notifications in dev within one send; flag off hides the toggle."

## User stories

| ID | Story | File |
|---|---|---|
| US-046-01 | Turn off my notification emails | `US-046-01-turn-off.md` |
| US-046-02 | Trust that recipients are unaffected | `US-046-02-recipients-unaffected.md` |
