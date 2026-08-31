# US-031-01 — Sign by drawing, typing, or uploading

**Feature:** F-SGN-002 — Sign | **Status:** pending

---

**Story:** As a signer, I want to sign with whatever is most natural — draw it, type my name, or upload an image — so that the signature moment has no friction, even on a phone.
**Actor:** signer (guest, no account).
**Goal:** the signature pad with three modes — **draw** (canvas), **type** (rendered to the same PNG), **upload** (image ≤ 1 MB) — and one **Sign** button that persists the result.

## Preconditions

- The document is `Sending`, it is the signer's turn, and their token URL is valid (FR-031-4).

## Happy path

1. Open the sign URL: the document's pages render read-only, the signature pad shows three tabs (Draw / Type / Upload), and the **Sign** button is primary.
2. **Draw**: 300×150 canvas, finger or mouse; **Clear** resets the pad.
3. **Type**: name input + font select (system `cursive` stack — one script face, no external font, documented); the preview renders what will be stored.
4. **Upload**: image ≤ 1 MB, PNG/JPEG/WebP; preview replaces the pad.
5. **Sign** → `POST /api/v1/sign/{linkId}/sign/{token}/signature` (endpoint 37) → 201: `Signature` row (`Method` set), audit entry written in the same transaction (F-SGN-003), `document.signed` event.

## Alternative flows

- **Drawn signature empty** (user never touched the pad): **Sign** stays disabled until a mode has content (not an error after the fact).
- **Uploaded image 2 MB**: rejected with `SIGNATURE_TOO_LARGE` (EC-031-3, TA-4.1.3) — inline, retryable.
- **Ancient browser without canvas** (EC-031-2): Type and Upload tabs still work; Draw is the default tab but not required.

## Acceptance criteria

```gherkin
Given it is my turn and the pad is in Draw mode
When I draw and press Sign
Then a Signature row exists with Method=Draw, AgreedOn set, and an audit entry in the same transaction

Given the pad is in Type mode with my name
When I press Sign
Then Method=Type and the rendered PNG is stored in the signature blob

Given the pad is in Upload mode with a 1 MB PNG
When I press Sign
Then Method=Upload and the uploaded image is stored
```

## Edge cases

- Mode switch discards the other mode's content (documented; a "you'll lose your drawing" hint only when leaving a dirty Draw pad — P2 nicety, not MVP).
- Double-click / refresh on **Sign**: single-use token → exactly one `Signature` row (AC-031-4, US-031-04 state).

## UI notes

- Signature pad card: three tabs, pad area 300×150 (draw) / preview (upload) / styled preview (type). **Sign** primary + **Decline** ghost (US-031-03).
- No drag-to-place in MVP: placement is page 1, bottom-left (FR-031-2) — the preview shows where the signature lands.

## Technical notes

- `Signature` DDL (F-SGN-002 TA-3.2 addition): `Method TINYINT (0 Draw, 1 Type, 2 Upload)`, `SigBlobRefId`, `PageNumber DEFAULT 1`, `SignedAtUtc`.
- Blob: `documents/{documentId}/signatures/{recipientId}.png` (TA-3.5 addition).
- Type mode renders client-side to the same PNG (canvas `fillText` with the cursive stack) — server stores bytes, not font state.
- Telemetry `signature_created { documentId, method }` (PII-safe).

## Links

- Feature: `SGN-002-sign.md` (FR-031-1, FR-031-2, AC-031-1/2, EC-031-2/3)
- Related: US-031-02 (the date stamp), US-031-04 (turn + token), F-SGN-003 (audit), F-SGN-004 (burn-in)
