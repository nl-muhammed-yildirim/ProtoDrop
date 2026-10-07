# T-003-03 — Terminal-state screens: expired / not-found / download-limit + "Downloads left" (FR-003-5/8/9)

**Story:** US-003-05 | **Spec:** FR-003-5/8/9, AC-003-3…003-5, TA-4.1.3/TA-7.2 | **Size:** M
**Depends on:** T-003-01 (endpoint 4 status mapping), T-003-02 (recipient page shell)

---

## Context to read (only these)

- `US-003-05-remaining-expired.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-003-recipient-page.md` → FR-003-8/9 + AC-003-3…003-5 + UI notes (terminal states line)

## Instructions

1. Build the three terminal screens in **`RecipientPage.tsx`**, each a single-screen shape per UI-Reference §4.6 (icon, one line, one action):
   - **Expired** (`Status=2`, 410): "This transfer has expired." + sender's email when provided + **Send something**.
   - **Unknown linkId** (404): the *identical* screen — deliberately indistinguishable from expired except HTTP status (FR-003-9, avoids ID enumeration).
   - **Download limit reached** (`Status=3`, 410): "All downloads have been used." + sender email + **Send something**.
2. When the sender provided no email: the screen says "Contact the sender for a new link." — never fabricate an address.
3. Add the **"Downloads left: N"** line under the file list in `--fs-small` (FR-003-5): visible only when the cap is finite and past the first download; Business (∞) hides it entirely.
4. Byte-leak audit: terminal screens must not request or render `FileItem` sizes beyond what the screen needs — endpoint 4 already returns a minimal payload for non-active states (T-003-01).

## Exit check

- [ ] Expired and unknown links render byte-identical screens; only HTTP status differs (410 vs 404)
- [ ] No file list, no sizes, no SAS mint on any terminal screen
- [ ] "Downloads left: N" shows the correct remaining count for finite caps and is hidden for ∞

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web + wa-api; endpoint 4 status mapping in place).
Task T-003-03 — build the terminal-state screens.
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-05-remaining-expired/US-003-05-remaining-expired.md (happy path + Alternative flows + Edge cases) and F-TRF-003-recipient-page.md (FR-003-8/9).
Do exactly:
1. Build the three terminal screens in RecipientPage.tsx, each a single-screen shape per UI-Reference §4.6 (icon, one line, one action): Expired (Status=2, 410) "This transfer has expired." + sender's email when provided + Send something; Unknown linkId (404) the identical screen — deliberately indistinguishable from expired except HTTP status (FR-003-9); Download limit reached (Status=3, 410) "All downloads have been used." + sender email + Send something.
2. When the sender provided no email: the screen says "Contact the sender for a new link." — never fabricate an address.
3. Add the "Downloads left: N" line under the file list in --fs-small (FR-003-5): visible only when the cap is finite and past the first download; Business (infinity) hides it entirely.
4. Keep the byte-leak audit clean: terminal screens must not request or render FileItem sizes beyond what the screen needs — endpoint 4 already returns a minimal payload for non-active states (T-003-01).
Done when: AC-003-3…AC-003-5 hold client-side — expired and unknown render identical screens with only the HTTP status differing, no file list or SAS on terminal screens, and the downloads-left line behaves as specified.
Constraints: use design tokens only; the Send something CTA is wired by T-003-08 (growth loop) — at this task it may be a placeholder anchor to /.
```
