# US-002-01 — Get a unique link for my files

**Feature:** F-TRF-002 — Transfer Creation & Link | **Status:** pending

---

**Story:** As a sender, I want a short, clean link I can paste anywhere, without a URL bar full of tracking noise, so that I can share my files in any channel (chat, email, document).
**Actor:** Guest or signed-in user who has finished uploading (F-TRF-001).
**Goal:** Receive a permanent, unguessable link that uniquely identifies the transfer.

## Preconditions

- All staged files uploaded (100 %) and the draft exists.

## Happy path

1. `FinalizeTransferCommand` runs (idempotency-keyed): server-side blob copy, `Transfer(Status=0)` + `FileItem` + `BlobRef(RefCount=1)` created.
2. The transfer receives an 8-char Crockford base32 `LinkId` (no `0/O/1/I`).
3. The link screen shows the full public URL `{origin}/t/{linkId}` in a copy field with a **Copy** button.
4. `GET /t/{linkId}` resolves (recipient page) as soon as the transfer is sent.
5. The link contains no query parameters, no tracking, no account hints.

## Alternative flows

- **Collision (astronomically unlikely):** the server regenerates the ID once; a second collision → 500 + `INTERNAL` telemetry (AC-002-4).
- **Double-click / double-POST finalize:** the second request carries the same `Idempotency-Key` and returns the *original* transfer — no second link is minted (EC-002-1).

## Acceptance criteria

```gherkin
Given a draft with 3 uploaded files
When the finalize request succeeds
Then the transfer has an 8-character Crockford linkId
And the copy field shows the full public URL

Given the link is active
When a recipient opens GET /t/{linkId}
Then HTTP 200 and the recipient page renders

Given I double-press "Send." (double finalize)
When the second request arrives with the same Idempotency-Key
Then the original transfer is returned
And exactly one linkId exists
```

## Edge cases

- Link ID space: 32^8 ≈ 1.1 × 10^12 — 40 bits of entropy; enumeration is not a practical threat (TA-9.6).
- The URL is stable for the life of the transfer (until expiry/deletion).
- Copy uses `navigator.clipboard` with `execCommand` fallback; success state "Copied ✓" for 2 s.

## UI notes

- Link screen card: "Your link is ready" + copy field (UI-Reference §4.4, §5.2).
- The link itself is the only "hero" element on the screen.
- Copy button per §4.5: primary-look inside the field, success state in `--success` text.

## Technical notes

- Domain `LinkId` value object in `wa.domain/ValueObjects`; generation in `FinalizeTransferCommandHandler`.
- `UQ_Transfer_LinkId` (TA-3.2) enforces uniqueness at the DB level.
- `Idempotency-Key` per TA-4.1.5 (SHA-256 hex, 24 h TTL, replay returns stored `ResultJson`).
- Telemetry: `transfer.created` emitted at *send* (not finalize) per TA-5.3.

## Links

- Feature: `TRF-002-transfer-link.md` (FR-002-1, FR-002-2, EC-002-1)
- Plan AC: AC-002-1, AC-002-4
- Architecture: TA-3.2, TA-4.1.5, TA-4.2#2, TA-7.1
- Design: UI-Reference §4.4, §5.2
- Milestone: T-010, T-013
