# QA-SCRIPTS.md — Manual Verification Scripts

**Last updated:** 2026-08-31
You (the human) are the QA for tasks whose exit checks say "manual pass in browser" (T-012, T-013, T-014, T-015, T-017, T-027, …). This file is the click-by-click script for each. Run the environment first: Docker up (`docker compose -f docker-compose.local.yml up -d`), API on `:8080` (F5), web on `:5173` (`npm run dev`).

**Rules of the run:**
1. Do the pass **fresh** — clear browser storage between scenarios (DevTools → Application → Clear site data), or use a private window, unless the script says otherwise.
2. Mark each scenario `PASS`/`FAIL` in the checkbox list at the end of the milestone section. A `FAIL` blocks the task's exit check.
3. Telemetry checks: Serilog console or App Insights dev workspace — the event name must be exactly the TA-10.2 name (e.g. `password_correct`).
4. "Force a condition" helpers are listed once in §0 and referenced by name below.

---

## 0. Helper: forcing conditions in dev

| Helper | How |
|---|---|
| **Force a 5 GB upload** | `fsutil file createNew big.dat 5368709120` (or `New-Item -Path big.dat -ItemType File -Value $([Text.UTF8Encoding]::GetEncoding().GetBytes((New-Object char[] 1024)) | ...)` — `fsutil` is simplest) → use it as the upload file. For a >5 GB file (over-limit test), create `big2.dat` at `5368709121` bytes. |
| **Force expiry now** | Set `ExpiresAtUtc = GETDATE()-1s` on the `Transfer` row in SSMS (Docker SQL), or use the admin flag override path if F-TRF-011 flags screen exists. |
| **Force expired storage deletion** | After expiry, set `ExpiredAtUtc` to 4 days ago (`GRACE_DAYS` is 3) so `f-delete-transfers` picks it up at its next 15-min tick. |
| **Force download-limit reached** | Set `DownloadsCount = 99` and `MaxDownloads = 100` — or set `MaxDownloads = 0` + `DownloadsCount = 1` for an immediate cap. |
| **Force failed ad host** | Block the ad SDK host in the browser (DevTools network → blocklist) or set the flag `feature.ads` off/on via admin flags screen. |
| **Force email down** | Set `Wa:CommunicationHub:Connection` to a bad value in the Local profile, or stop Azurite for blob-down tests. |
| **Fake clock** | Where the spec says "fake clock", the dev override is the env var documented in the task (e.g. `Wa:Time:NowUtc` if the task implements it) — if the task hasn't implemented one, say so in `PROGRESS.md` instead of guessing. |

---

## M1 — Upload & Link (T-012, T-013)

### S-1.1 Guest upload, happy path (T-012)
1. `http://localhost:5173` — landing page renders, drop zone visible.
2. Drag in 3 files (one small, one ≥100 MB if you have one, one with spaces + a unicode name).
3. Check: per-file rows appear with correct name/size; overall progress bar moves; no console errors.
4. Upload completes → **Send.** button enabled. Click it.
5. Check: link screen shows a `…/t/{8-char linkId}` URL with a copy field; copy button copies exactly that URL.

**PASS criteria:** AC-001-1…001-4 — drop/paste/click all select; pre-check blocks a file >5 GB *before* upload; progress is per-file **and** overall; retry recovers an injected failure (DevTools → Network → set "Offline" mid-upload once, watch block-level retry, go back online).

### S-1.2 Injected upload failure (T-012 exit check)
1. Start a ≥200 MB file upload.
2. At ~30 %, DevTools → Network → throttle to "Offline" for 5 s, then back online.
3. Check: upload shows retrying, then completes; the transfer finalizes with all files present (no partial file in the transfer).

### S-1.3 Send form + confirmation (T-013)
1. From the link screen, add 2 recipient emails, a password, a note, sender name.
2. Click **Send transfer.**
3. Check: confirmation screen renders (link + "sent" state); `GET /t/{linkId}` now shows `passwordRequired: true`.
4. Send a second transfer **link-only** (no emails) — allowed, link works.

**PASS criteria:** AC-002-1…002-3 (email validation/normalization: mixed-case + unlisted domains normalized; bad emails rejected with inline message); copy field copies verbatim; `/t/{linkId}/sent` renders.

---

## M2 — Download, Expiry, Email (T-014…T-016)

### S-2.1 Recipient page states (T-014)
1. Open a fresh transfer link → **active** screen: file list, `downloadsLeft`, download buttons.
2. Open a **bogus** link (`/t/AAAAAAAA`) → not-found screen.
3. Use the "force expiry now" helper on a real transfer → open its link → **expired** screen.
4. Check: 404 vs 410 response **bodies are identical** (DevTools → both show the same JSON except status code) — AC-003-3/003-4.
5. Set download cap (helper) → open link → **download-limit** screen, no file list leak.

### S-2.2 Password gate (T-015)
1. Open a password-protected link.
2. Enter a **wrong** password → shake animation + `password_wrong` event in telemetry.
3. Enter the correct one → unlocks; `password_correct` event.
4. **Refresh the page** → still unlocked (sessionStorage token), no re-prompt — AC-003-2.
5. Clear site data → re-open → prompted again.

### S-2.3 Single-file download + resume (T-016)
1. Download one file from a ≥500 MB transfer.
2. Pause the download at ~50 % (browser download UI or DevTools), resume.
3. Check: resume uses a Range request (DevTools → Request Headers show `Range: bytes=…`); file completes and opens.
4. Check: `DownloadsCount` incremented exactly once for the completed download.

### S-2.4 Download all (zip) — T-017 (see M2 zip script in §3 if T-017 is not yet built; otherwise run the Playwright step of the task)

---

## M2/M3 — Limits (T-020)

### S-3.1 Free-tier limit screens
1. As a Free user, attempt to send a 5 GB+1 transfer → limit screen at **pre-check** (client) — and again via direct API call with the same payload (server must reject; "server-enforcement" story).
2. Fill storage quota (create transfers until `STORAGE_QUOTA` is hit — use small files, many of them, or temporarily lower the flag value via admin to make it fast).
3. Check: "storage full" screen states the limit; admin overview shows the quota meter.

---

## M3 — Accounts (T-021)

### S-4.1 Auth loop
1. Sign up (email + password) → confirmation, then logged in.
2. Log out → land on anonymous state; **magic link** login works (check the dev log for the link).
3. Forgot-password → link → reset.
4. `GET /auth/me` returns profile; `PATCH` changes name/theme; `DELETE` deletes the account → **transfers survive** (open a link created before deletion; blob intact) — EC-008-2.

---

## M4 — Hardening (T-026, T-027)

### S-5.1 i18n spot-check (T-026)
1. Switch browser language to `de` → reload → UI in German; no missing-key placeholders (`translation_missing` must be zero in telemetry for the P0 screens).
2. Force each of the 8 launch locales via URL/browser setting — landing + recipient + error screens render in-language.

### S-5.2 Dark mode + keyboard-only (T-027)
1. Light → dark → system: no flash of wrong theme on reload (check the first painted frame).
2. **Keyboard-only pass:** Tab through the entire guest flow (upload → send → recipient page → download). No focus traps, visible focus ring on every interactive element.
3. Screen reader (Narrator/NVDA): drop zone, progress, buttons are announced.

### S-5.3 Mobile web matrix (T-027)
- DevTools device toolbar: iPhone 13, Android (Pixel).
- Send a transfer from the phone-sized viewport; open it from a second context (different device profile) → download works.

---

## M4.5 — Phase 1 quick checks (when Phase 1 tasks land)

- **Plan gate:** as Free, try to schedule (T-038) → "Scheduling requires Pro"; upgrade via Stripe test mode (D-12) → allowed within 60 s.
- **Billing emails:** Stripe test portal → mark card failed → expect past-due emails at 0/3/7 d offsets (fake clock).
- **Ads off by default (D-14):** recipient page as Free shows **no** ad slot; block-list the ad host → if a flag is on, slot collapses invisibly.

## M5 — Phase 2 quick checks (when Phase 2 tasks land)

- **Collect:** Free user → "Collect requires Pro"; Pro user creates collection; guest submits without account; due date + 7 d grace banner states render.
- **Sign:** signer 1 can sign now, signer 2 sees "not your turn"; docx sent → converted to PDF (check `DOCX_CONVERT_FAILED` does not fire); void → pending signers' emails stop.
- **Albums:** album link still loads after 30 days; contributor upload works with re-issued token; lightbox swipe on mobile profile.

---

## Runbook per task

1. Find the task's **exit check** in `Milestone-Backlog.md`.
2. Run the matching scenario(s) above.
3. Record `PASS`/`FAIL` + date in `PROGRESS.md` session notes.
4. `FAIL` → back to the AI session with the exact repro steps in `PROGRESS.md` "Known open items".
