# US-046-01 — Turn off my notification emails

**Feature:** F-XCT-003 — Email Settings (Notification Preferences) | **Status:** pending

---

**Story:** As a user who re-sends transfers daily, I want one switch that stops the "your transfer expired / was downloaded" emails, so that my inbox only gets what I asked for — without deleting my account to do it.
**Actor:** Signed-in user in Profile.
**Goal:** One toggle in Profile → my account's notification emails stop arriving; recipients and billing are untouched.

## Preconditions

- Signed in; `feature.emailSettings` is true (F-XCT-001 gate, US-044-02).
- The user has at least one active transfer (the setting is meaningful only with notifications to expect).

## Happy path

1. Profile → **Notification preferences** → toggle "Email me about my transfers and accounts" off.
2. `PATCH /api/v1/auth/me {"emailNotifications": false}` → saved; toast "Notification preferences saved."
3. From the next send on, `f-email` skips notification emails owned by this account; no `email_sent` is emitted for them and the skip is logged (`suppressedReason=email_setting`).
4. Toggling back on restores sending; nothing else changed.

## Alternative flows

- **Flag off in the environment:** the toggle is absent — the user is on the all-on behavior (US-044-02 contract).
- **Guest:** no Profile → no toggle; all their sends happen as today (FR-046-5).
- **Forgot password / magic link:** still sent — auth emails are not "notifications" (they're required to regain access; documented).

## Acceptance criteria

```gherkin
Given my account setting is on
When I turn it off in Profile and save
Then my setting is false and the next owner notification email is skipped
And no email_sent event is emitted for the skipped send
And my recipients still receive their download emails

Given I turn the setting back on
When the next owner notification is due
Then it is sent normally
```

## Edge cases

- A notification already queued when I toggle may still arrive (EC-046-1: the worker reads the setting at send time; one message window).
- Account deleted and re-created → the setting is on again (NULL default, EC-046-2).
- The helper text must name the two exemptions out loud: **recipients** and **billing** (FR-046-4) — this is the copy that prevents support tickets.

## UI notes

- Profile → Notification preferences: single switch, immediate save, toast on change.
- Copy: "Email me about my transfers and accounts. Recipients always get their download emails; billing emails always arrive."

## Technical notes

- `AppUser.EmailNotificationsEnabled` NULL=on, 0=off (FR-046-1); `PATCH /auth/me` additive merge (endpoint 13).
- Worker: `f-email` joins `AppUser` at send time; skip → Information log, no event (FR-046-3).

## Links

- Feature: `XCT-003-email-settings.md` (FR-046-1/2/3/5/6)
- Plan AC: AC-046-1, AC-046-2
- Related: US-006-03 (recipient unsubscription — the different, per-sender mechanism), US-008-04 (Profile page), US-044-02 (the gate)
- Architecture: TA-4.2 (endpoint 13), TA-9.4
- Milestone: T-067
