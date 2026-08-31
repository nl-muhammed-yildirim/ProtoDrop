# US-033-02 — Get the final document without asking

**Feature:** F-SGN-004 — Final Document | **Status:** pending

---

**Story:** As a signer (or the sender), I want the final document emailed to me when it exists — every party, exactly one email each — so that nobody has to check back to find out it's done.
**Actor:** sender, all signers.
**Goal:** on `document.completed`, every party (sender + all signers) receives exactly one `final_ready` email with a working 7-day download link (FR-033-3).

## Preconditions

- The document completed (US-033-01) and the final PDF exists.

## Happy path

1. `document.completed` → `f-email` fan-out (F-TRF-006 pipeline: retries, suppression, dead-letter, localization).
2. Each address — sender + every signer, including declined ones' *signers* (they signed; declined parties are named, documented) — gets one email: "Your signed document {title} is ready" + one CTA button (the 7-day SAS, TA-3.6 addition for final documents).
3. Duplicate events (replay) → exactly one email per address per document (idempotency per (document, address), TA-5.2).

## Alternative flows

- **Bounced address**: `email.failed` marks it; the other parties still get theirs (state and delivery are decoupled — same rule as F-COL-005 EC-029-2).
- **Suppressed address**: no email, visible in admin (F-TRF-006-5 / F-TRF-011) — no "sent but ignored" ghost.

## Acceptance criteria

```gherkin
Given the last signer signs and the final PDF exists
When the email fan-out runs
Then sender and every signer each receive exactly one final_ready email with a working link

Given the completed event is replayed
When the fan-out runs again
Then no additional emails are sent
```

## Edge cases

- The 7-day SAS (vs. the 30-minute page SAS) is deliberate: the email must outlive the page session (TA-3.6 addition, ADR note).
- One CTA button per email, calm tone, no exclamation points (F-TRF-006 design rule).

## UI notes

- Email: single button "Download the signed document" + file name + signer list with dates (the social proof that it's the *full* document).
- No inline preview image in MVP (the 7-day SAS is the download, not an <img> — hotlink hygiene, documented).

## Technical notes

- Template `final_ready` (F-TRF-006 template family); event `document.completed` consumed by `f-email` (subscription `email`).
- Idempotency: per (documentId, address) — a second `document.completed` for the same document is a no-op.
- Telemetry: `email.sent` / `email.failed` per F-TRF-006 — no new event.

## Links

- Feature: `SGN-004-final-document.md` (FR-033-3, AC-033-2)
- Related: F-TRF-006 (pipeline), US-033-01 (what the link downloads), US-029-02 (same fan-out discipline in Collect)
