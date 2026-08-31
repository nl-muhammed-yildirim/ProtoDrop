# US-021-02 — Brand my emails too

**Feature:** F-PRF-001 — Custom "From" Branding | **Status:** pending

---

**Story:** As a Pro user, I want my organization name to appear in the notification email's sender line, so that the email matches the branded page.
**Actor:** Pro/Business user, email recipients.
**Goal:** email subject/`From:` reflect the org ("Transfer from {orgName} — {personName}"), consistent with the page.

## Preconditions

- User on Pro/Business with org name set.

## Happy path

1. Pro user with org "Studio Nova" sends a transfer.
2. Recipient email subject: "Transfer from Studio Nova — Anna K."
3. `From` remains `no-reply@{domain}` (D-04); the org line is in the subject + body header.

## Alternative flows

- **No per-transfer sender name**: subject "Transfer from Studio Nova" (org only).
- **Free tier**: plain "Transfer from {senderName}" (no org, FR-021-4).

## Acceptance criteria

```gherkin
Given a pro user set org "Studio Nova"
When a recipient is emailed
Then the subject includes the org name
And the body header shows the org

Given a free user
When a recipient is emailed
Then the subject is the plain sender name (no org)
```

## Edge cases

- Org changed after send → email keeps the **send-time** org (page re-renders current, email is a snapshot — documented divergence, acceptable because email is one-shot).
- Org name PII: hashed in telemetry (TA-9.4); raw in the PII-flagged property.

## UI notes

- Email header line: "From Studio Nova — Anna K." (calm tone, no exclamation points).

## Technical notes

- Email template reads org from the transfer's snapshot at send (F-TRF-006 pipeline).
- `From` is always `no-reply@{domain}` (D-04) — org is content, not the mailbox.

## Links

- Feature: `PRF-001-from-branding.md` (FR-021-1, AC-021-1)
- Related: F-TRF-006 (email pipeline), F-BIL-001 (branding gate)
- Decisions: D-04
