# T-006-03 — Unsubscribe: /unsubscribe/{token} + EmailSuppression (FR-006-5)

**Story:** US-006-03 | **Spec:** FR-006-5, AC-006-3, TA-6.7/TA-3.2 | **Size:** M
**Depends on:** T-006-01 (f-email — the suppression check already reads `EmailSuppression`)

---

## Context to read (only these)

- `US-006-03-unsubscribe.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-006-email.md` → FR-006-5 + AC-006-3 + UI notes (unsubscribe landing line)

## Instructions

1. Add **endpoint** (public, no cookie): `GET /unsubscribe/{token}` where token = **HMAC of the suppression row id**. Validate; on success insert **`EmailSuppression (Address, SenderEmail, CreatedAtUtc)`** — idempotent via the unique constraint `UQ_ES`.
2. Redirect to `/` with a success toast: **"You are on the list no more."** Landing page = centered card + **Go to ProtoDrop** button (UI-Reference §4.6 shape).
3. Tampered/unknown token → **404** (do not leak "token invalid" detail) + warn telemetry.
4. Future sends: `f-email`'s suppression lookup (T-006-01) skips this address+sender pair; other senders still email the same address (per-sender design).
5. Admin can list/remove suppression rows (F-TRF-011, US-011-04); removal emits `admin_action` telemetry — that admin UI is F-TRF-011's scope; here just make the row queryable/removable via API.

## Exit check

- [ ] Unsubscribe → `EmailSuppression` row exists for (address, sender); a later transfer from the same sender sends no email
- [ ] The address still receives emails from other senders
- [ ] Second click / stale token → no-op with the same success landing page
- [ ] Tampered token → 404 + warn telemetry

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa-api public routes, EmailSuppression table in place).
Task T-006-03 — implement the unsubscribe route.
Read first (only): docs/features/Phase 0-MVP/F-TRF-006/US-006-03-unsubscribe/US-006-03-unsubscribe.md (happy path + Alternative flows + Edge cases) and F-TRF-006-email.md (FR-006-5).
Do exactly:
1. Add a public endpoint (no cookie): GET /unsubscribe/{token} where token = HMAC of the suppression row id. Validate; on success insert EmailSuppression (Address, SenderEmail, CreatedAtUtc) — idempotent via the unique constraint UQ_ES.
2. Redirect to / with a success toast: "You are on the list no more." Landing page = centered card + Go to ProtoDrop button (UI-Reference §4.6 shape).
3. Tampered/unknown token → 404 (do not leak "token invalid" detail) + warn telemetry.
4. Keep suppression per (address, sender): f-email's lookup (T-006-01) skips this pair; other senders still email the same address.
5. Make the row queryable/removable via API for admin use (F-TRF-011, US-011-04); removal emits admin_action telemetry — the admin UI itself is F-TRF-011's scope.
Done when: AC-006-3 holds client-side and server-side — unsubscribing mutes that sender only, double-clicks are no-ops with the same landing page, tampered tokens 404 without leaking detail, and a later transfer from the same sender sends nothing.
Constraints: token = HMAC(suppressionId) (TA-6.7); the route is served by the SPA (public, no cookie needed); suppression created mid-batch can at most save or miss one email (EC-006-3 — documented, do not lock for it).
```
