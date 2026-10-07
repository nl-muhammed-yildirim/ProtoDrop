# T-006-01 — f-email consumer: template render, CH send, dedup + skips (TA-6.7)

**Story:** US-006-01 (+ US-006-04 headers) | **Spec:** FR-006-1/2/3/6/7/8, AC-006-1/4, TA-6.7 | **Size:** M
**Depends on:** T-002-04 (send endpoint emits `transfer.created`), T-052-03 (domain + WaDbContext)

---

## Context to read (only these)

- `US-006-01-recipient-email.md` → happy path + Alternative flows + Technical notes
- `US-006-04-reply-branding.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-006-email.md` → FR-006-1/2/3/6/7/8 + AC-006-1/4 + Test plan (integration line)

## Instructions

1. Add **`f-email`** to `wa.workers`: consumes `core/email`, concurrency 10, 5-min timeout (TA-6.2). On `transfer.created` with a non-empty `emails[]`:
   - Skip the sender's own address (case-insensitive compare against `Transfer.SenderEmail`, FR-006-3).
   - Skip suppressed `(Address, SenderEmail)` pairs (`EmailSuppression` lookup, TA-3.2 unique constraint).
   - Dedup: event consumed twice → skip via `eventId` dedup + non-null `NotifiedAtUtc` (EC-006-4).
   - Link-only transfers (`emails: []`) short-circuit — no per-address work (FR-006-6, AC-006-4).
2. Render the template **Stubble** from `Emails/templates/transfer/{lang}.html` + `.txt` — one place, never inline in code (TA-6.7); subject `Transfer from {senderName}`; body = sender name, file list (names + human sizes), single CTA **Download files** → `{origin}/t/{linkId}`, footer "This link expires in N days." + unsubscribe link.
3. Send via Communication Hub: **`From = no-reply@{domain}`** (config `Wa:Email:FromAddress`, D-04); **`ReplyTo = Transfer.SenderEmail ?? null`** — read from the transfer row, not the session (US-006-04).
4. On success: set `EmailRecipient.NotifiedAtUtc` (exactly-once via idempotent consumer), emit **`email.sent`**.

## Exit check

- [ ] 3 addresses incl. sender's own → exactly 2 sends; each email has the file list + single CTA + correct expiry days
- [ ] Link-only transfer (`emails: []`) → zero sends
- [ ] `From = no-reply@{domain}` always; `Reply-To` present only when SenderEmail exists (US-006-04 AC)
- [ ] Replayed event with non-null `NotifiedAtUtc` → no second send

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.workers project, core/email subscription + EmailRecipient rows in place).
Task T-006-01 — implement the f-email consumer.
Read first (only): docs/features/Phase 0-MVP/F-TRF-006/US-006-01-recipient-email/US-006-01-recipient-email.md (happy path + Alternative flows + Technical notes), US-006-04-reply-branding.md (Technical notes), and F-TRF-006-email.md (FR-006-1/2/3/6/7/8).
Do exactly:
1. Add f-email to wa.workers: consumes core/email, concurrency 10, 5-min timeout (TA-6.2). On transfer.created with a non-empty emails[]: skip the sender's own address (case-insensitive compare against Transfer.SenderEmail, FR-006-3); skip suppressed (Address, SenderEmail) pairs (EmailSuppression lookup, TA-3.2 unique constraint); dedup via eventId + non-null NotifiedAtUtc (EC-006-4); link-only transfers (emails: []) short-circuit — no per-address work (FR-006-6, AC-006-4).
2. Render the template Stubble from Emails/templates/transfer/{lang}.html + .txt — one place, never inline in code (TA-6.7); subject "Transfer from {senderName}"; body = sender name, file list (names + human sizes), single CTA Download files → {origin}/t/{linkId}, footer "This link expires in N days." + unsubscribe link.
3. Send via Communication Hub: From = no-reply@{domain} (config Wa:Email:FromAddress, D-04); ReplyTo = Transfer.SenderEmail ?? null — read from the transfer row, not the session (US-006-04).
4. On success: set EmailRecipient.NotifiedAtUtc (exactly-once via idempotent consumer), emit email.sent.
Done when: AC-006-1 and AC-006-4 hold — exactly one email per non-skipped address, link-only sends nothing, the From/Reply-To headers match US-006-04's ACs, and a replayed event does not double-send.
Constraints: templates live in Emails/templates only (TA-6.7); dev environment uses Wa:Email:Sender = log (log-only sender switch — T-019 exit check); +1/+2 Gmail aliases are distinct recipients at M0 (EC-006-2).
```
