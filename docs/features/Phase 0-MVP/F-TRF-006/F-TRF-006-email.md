# F-TRF-006 — Email Notifications

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-006 | **Architecture:** TA-6.7, TA-5.3, TA-3.2 (`EmailRecipient`, `EmailSuppression`)
**Milestone tasks:** T-019

---

## Description

The link is only half the product — the other half is getting it into the recipient's inbox. On `transfer.created`, the `f-email` function sends **one clear, minimal email per recipient address** through Azure Communication Hub: sender name, file list with sizes, a single "Download files" button, and an expiry footer. Delivery is resilient (retries with backoff, then dead-letter + alert), and recipients can permanently mute a sender (per-sender suppression). Email is **eventually consistent**: when it's down, transfers still succeed.

**Actors:** recipient (gets the email), sender (their name/reply-to is on it), operator (sees failure health).
**Value:** recipients with zero context can get their files in one tap from the inbox.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-006-1 | On `transfer.created` (when emails were provided): **one email per address** via Azure Communication Hub (no shared multi-recipient send — one `EmailRecipient` row per address). |
| FR-006-2 | Content: subject `Transfer from {senderName}`; plain text + minimal inline HTML: sender name, file list (names + human sizes), single CTA button **Download files** → public URL, footer "This link expires in N days." + unsubscribe link. |
| FR-006-3 | The **sender's own address** (if present in the recipient list) is stored but not emailed (skip in `f-email`). |
| FR-006-4 | Delivery failure → retries 3× on the Service Bus delivery cycle (approx. 1 min / 10 min / 1 h); final failure → dead-letter + `email.failed` telemetry + admin alert (`DLQ_COUNT > 0`). |
| FR-006-5 | Unsubscribe link (`/unsubscribe/{token}`, token = HMAC of the suppression row id): inserts `EmailSuppression (Address, SenderEmail)`; redirect to `/` with a success toast. Suppressed address+sender gets no future emails; the row is visible/removable in admin (F-TRF-011). |
| FR-006-6 | **No email for link-only transfers** (`emails: []` in `transfer.created` → worker skips). |
| FR-006-7 | All notification emails use the sender's email as `Reply-To` when present; `From` is always `no-reply@{domain}` (D-04). |
| FR-006-8 | On success: `EmailRecipient.NotifiedAtUtc` is set (exactly-once via idempotent consumer — re-delivery with a non-null `NotifiedAtUtc` skips). |

## Acceptance criteria

```gherkin
AC-006-1: Send transfer to 2 addresses including the sender's own
  Then exactly 1 email is sent

AC-006-2: Communication Hub returns 429
  Then the message is retried 3 times with backoff, and a 4th failure dead-letters it
  And email_failed is emitted and the DLQ alert fires

AC-006-3: Recipient clicks unsubscribe
  Then subsequent transfers from the same sender email do not notify them
  And the suppression is visible in the admin dashboard

AC-006-4: Transfer sent with zero emails
  Then no email worker run produces a send (emails: [] short-circuits)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-006-1 | More than `MAX_EMAILS` (Free: 20) | UI enforces; the API also caps at send (defense in depth, F-TRF-007) |
| EC-006-2 | `+1` / `+2` Gmail alias addresses | Treated as **distinct** recipients (documented; no alias canonicalization in MVP) |
| EC-006-3 | Recipient address later suppressed mid-batch | Suppression is checked at send time in `f-email`; worst case one extra email (documented) |
| EC-006-4 | Same `transfer.created` event consumed twice | Consumer dedups by `eventId` (TA-5.2) + `NotifiedAtUtc` check — no double email |

## UI notes (UI-Reference §5.2, §7)

- Link screen recipient field helper: "One per line — you can also separate with commas." (the *sending* UX belongs to F-TRF-002; this feature owns the outbound email).
- Email design: one CTA button, calm tone, no exclamation points; dark-mode-safe minimal HTML (inline styles, 600 px max-width).
- Unsubscribe landing: centered card "You are on the list no more." + **Go to ProtoDrop** button.

## Technical notes

- Consumer: `f-email` on `core/email` subscription, concurrency 10, 5-min timeout (TA-6.2).
- Templates: `Emails/templates/transfer/{lang}.html` + `.txt` (Stubble/Mustache) — **one place, never inline in code** (TA-6.7); language selection per F-TRF-014-5 (US-006-05).
- Send: Communication Hub (`Azure.Communication.Email`), `From = no-reply@{domain}` (D-04), `ReplyTo = Transfer.SenderEmail` when present.
- Suppression check: `EmailSuppression` lookup on `(Address, SenderEmail)` before each send (TA-3.2 unique constraint).
- Events: `email.sent` / `email.failed` (`{transferId, to, attempt, reason?}`); metric `email_failures_1h`; alert threshold 50/h (TA-10.3).
- Dev environment: log-only sender switch (`Wa:Email:Sender = log`), visible in dev run (T-019 exit check).
- Retries use the SB delivery count (MaxDeliveryCount 5 ⇒ 3 visible retries after first attempt); final attempt failure → move to DLQ (30-day retention) + `email.failed`.

## Test plan

- Unit: template rendering (file list formatting, byte humanization), subject line, suppression matching, sender-self-skip.
- Integration: `transfer.created` with 2 emails + sender's own → exactly 1 `CommunicationHubEmailSender.SendEmail` call; 429 → retry sequence observable via fake sender (3 attempts); DLQ path; `NotifiedAtUtc` set; replay → no second send.
- E2E: dev log-sender run shows the rendered email (manual visual check); unsubscribe round-trip (link → suppression row → admin list).
- Load: 1k emails in one burst → all delivered under 60 s p95 lag target (TA-15).

## User stories

| ID | Story | File |
|---|---|---|
| US-006-01 | Recipients get a notification email with the link | `US-006-01-recipient-email.md` |
| US-006-02 | Failed deliveries retry and dead-letter | `US-006-02-retry-delivery.md` |
| US-006-03 | Unsubscribe from a sender's emails | `US-006-03-unsubscribe.md` |
| US-006-04 | Reply goes to the sender (branded email) | `US-006-04-reply-branding.md` |
| US-006-05 | Receive the email in my language | `US-006-05-localized-email.md` |
