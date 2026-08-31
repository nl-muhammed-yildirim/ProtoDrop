# US-019-05 — Keep working during the 14-day grace

**Feature:** F-BIL-002 — Stripe Subscription Lifecycle | **Status:** pending

---

**Story:** As a customer whose subscription paused, I want a 14-day grace before downgrading to Free, so that a brief payment problem doesn't instantly cost me my plan or break my live transfers.
**Actor:** paying customer in `paused`/grace state.
**Goal:** `GraceEndsAtUtc = now + 14 d`; the plan survives the grace; on expiry, a clean downgrade to Free with an email.

## Preconditions

- Subscription is `paused` (from the billing sequence, US-019-04).

## Happy path

1. On `paused` → `Subscription.GraceEndsAtUtc = now + 14 d`.
2. During grace the user **keeps Pro** (limits, features, live transfers).
3. At `GraceEndsAtUtc` the 15-minute timer downgrades `AppUser.PlanId = free`, emits `plan.changed`, sends "You're back on Free".

## Alternative flows

- **Resumed during grace**: `resumed` clears `GraceEndsAtUtc`, restores Pro; pending "back on Free" email cancelled (EC-019-3, US-019-06).
- **Active transfers during grace/downgrade**: keep their send-time limits; new sends use the (downgraded) plan (F-BIL-003-2).

## Acceptance criteria

```gherkin
Given my subscription is paused
When I check my plan during grace
Then I am still on Pro

When grace ends
Then I am downgraded to Free
And a "You're back on Free" email is sent
```

## Edge cases

- 14-day grace for all plans (GRACE constant — F-BIL-002-6).
- Timer double-run in the same period → `JobRun` de-dup (TA-6.1), one downgrade.
- Downgrade with over-quota active transfers → they finish naturally (US-020-02).

## UI notes

- Banner during grace: "You have {n} days to fix your billing" (counting down, UI-Reference §5.7).
- No surprise: the plan screen still shows Pro during grace, with the banner.

## Technical notes

- Grace sub-step in the `f-expire` 15-minute pass (TA-6.3); `JobRun` claim key for de-dup (TA-6.1).
- `plan.changed` (TA-5.3) on the downgrade; "back on Free" email via `f-email` (`billing_grace_ended`).

## Links

- Feature: `BIL-002-stripe-lifecycle.md` (FR-019-2, FR-019-6, AC-019-2)
- Architecture: TA-6.1, TA-6.3, TA-3.2
- Related: US-019-04 (sequence), US-019-06 (restore), F-BIL-003 (over-quota)
