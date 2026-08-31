# US-046-02 — Trust that recipients are unaffected

**Feature:** F-XCT-003 — Email Settings (Notification Preferences) | **Status:** pending

---

**Story:** As a sender who turned off my own notifications, I want to be sure my *recipients* still get their download emails — and that I can explain the boundary in one sentence.
**Actor:** Signed-in user (and, secondarily, a support agent quoting the same sentence).
**Goal:** The suppression scope is explicit: my setting mutes **me**, never **them**; billing is always on.

## Preconditions

- The user has the setting off (or is about to turn it off).
- At least one transfer with recipients exists (F-TRF-006).

## Happy path

1. User reads the settings helper: "Recipients always get their download emails; billing emails always arrive."
2. A recipient receives the transfer email even though the sender's setting is off.
3. A Pro renewal invoice reaches the sender despite the off setting.
4. If a *recipient* gets too many emails, their remedy is the per-sender **Unsubscribe** footer link (F-TRF-006-5) — not the sender's setting.

## Alternative flows

- **Support question "why did I get this?":** answer = recipient-side unsubscription (per address+sender), visible/removable in admin (F-TRF-011).
- **The user turned it off expecting recipient silence:** documented boundary (EC-046-3); the settings copy is the contract.

## Acceptance criteria

```gherkin
Given my setting is off and I send a transfer to r@x.com
When f-email processes the transfer event
Then r@x.com receives the notification email
And no email is sent to my own address for that transfer's lifecycle events

Given my setting is off
When a billing event for my Pro plan fires (F-BIL-002)
Then the billing email is sent to me
```

## Edge cases

- Recipient unsubscriptions (`EmailSuppression`) are independent rows — the setting never deletes them, and vice versa.
- The boundary is stated in **one sentence** in three places: settings copy, feature spec (FR-046-4), and this story — keep them in sync if it ever changes.

## UI notes

- No new UI beyond the helper text from US-046-01; this story is the *scope* of the setting, verified by the worker's behavior.

## Technical notes

- `f-email` branch order: (1) recipient emails — no setting check (they're not the owner); (2) owner notifications — setting check; (3) billing — explicit exempt list (F-BIL-002 email types).
- Test fixture: one owner-off account, one transfer, three email types — asserts exactly one suppression path.

## Links

- Feature: `XCT-003-email-settings.md` (FR-046-3/4)
- Plan AC: AC-046-2, AC-046-3
- Related: US-006-01 (recipient email), US-006-03 (per-sender unsubscription), US-019-04 (billing emails)
- Architecture: TA-5.2 (event payload carries ownerAppUserId), TA-9.4
- Milestone: T-067
