# Features — Spec & User Story Index

**Last updated:** 2026-08-28
Every Phase-0 feature has **one spec file** and **one file per user story** — 66 stories, 100 % coverage of the F-TRF-001…017 index. **Phase 1 is fully specced too:** F-BIL-001…003 + F-PRF-001…004, 31 stories (US-018…024). **Phase 2 is fully specced:** F-COL-001…005 + F-SGN-001…004 + F-ALB-001…004, 44 stories (US-025…037). Phase 3 is fully specced too: F-ENT-001…006, 27 stories (US-038…043). **Phase X (cross-cutting) is fully specced:** F-XCT-001…005, 11 stories (US-044…048).

> **ID mapping (Phase X):** continues the per-feature numbering: F-XCT-001 → 044 · F-XCT-002 → 045 · F-XCT-003 → 046 · F-XCT-004 → 047 · F-XCT-005 → 048.

> **ID mapping (Phase 1):** Phase 0 occupies FR-001…017 / AC-0xx / US-0xx per feature number. Phase 1 continues the numbering per feature: F-BIL-001 → FR-018 / AC-018 / US-018; F-BIL-002 → 019; F-BIL-003 → 020; F-PRF-001 → 021; F-PRF-002 → 022; F-PRF-003 → 023; F-PRF-004 → 024. Phase 2 continues from 025.

---

## Conventions

- **Layout:** `Phase <n>-<name>/<feature-id>/` — each phase is a folder, each feature its own folder containing the feature spec **and** all of its user story files side by side.
- **Feature file:** `<feature-id>.md` — description, user & value, functional requirements (FR), acceptance criteria (Gherkin AC), edge cases (EC), UI notes, technical notes, test plan, user story index.
- **User story file:** `US-<nnn>-<ss>[-slug].md` — one file per story with: story, actor, goal, preconditions, happy path, alternative flows, acceptance criteria (Given/When/Then), edge cases, UI notes, technical notes, links.
- IDs are stable and cited in code, PRs, and the backlog (`Milestone-Backlog.md`).
- ACs here are the **canonical** acceptance criteria for the feature; they mirror and extend `02-feature-plan.md` (same AC IDs — no drift; if in doubt, this folder wins and the plan gets a note).
- "Definition of Done" for a story = its ACs green in automated tests + the feature file's test plan entry.

---

## Index

### F-TRF-001 — Upload Surface (chunked upload)
Spec: `Phase 0-MVP/F-TRF-001/F-TRF-001-upload-surface.md`

| Story | File |
|---|---|
| Select files with drag & drop, click, or paste | `Phase 0-MVP/F-TRF-001/US-001-01-select-files.md` |
| Stage and remove multiple files | `Phase 0-MVP/F-TRF-001/US-001-02-stage-files.md` |
| Get warned before upload exceeds the limit | `Phase 0-MVP/F-TRF-001/US-001-03-size-validation.md` |
| Watch per-file and overall progress | `Phase 0-MVP/F-TRF-001/US-001-04-upload-progress.md` |
| Recover from a failed upload | `Phase 0-MVP/F-TRF-001/US-001-05-retry-upload.md` |

### F-TRF-002 — Transfer Creation & Link
Spec: `Phase 0-MVP/F-TRF-002/F-TRF-002-transfer-link.md`

| Story | File |
|---|---|
| Get a unique link for my files | `Phase 0-MVP/F-TRF-002/US-002-01-unique-link.md` |
| Send the transfer to recipients by email | `Phase 0-MVP/F-TRF-002/US-002-02-send-by-email.md` |
| Protect my transfer with a password | `Phase 0-MVP/F-TRF-002/US-002-03-password.md` |
| Add sender info and a note | `Phase 0-MVP/F-TRF-002/US-002-04-sender-note.md` |
| Create a link-only transfer (no emails) | `Phase 0-MVP/F-TRF-002/US-002-05-link-only.md` |

### F-TRF-003 — Recipient Download Page
Spec: `Phase 0-MVP/F-TRF-003/F-TRF-003-recipient-page.md`

| Story | File |
|---|---|
| Open a transfer link with no account | `Phase 0-MVP/F-TRF-003/US-003-01-open-link.md` |
| Download all files in one zip | `Phase 0-MVP/F-TRF-003/US-003-02-download-all.md` |
| Download individual files | `Phase 0-MVP/F-TRF-003/US-003-03-download-individual.md` |
| Unlock a password-protected transfer | `Phase 0-MVP/F-TRF-003/US-003-04-unlock-password.md` |
| See remaining downloads and expired states | `Phase 0-MVP/F-TRF-003/US-003-05-remaining-expired.md` |
| Download from a phone | `Phase 0-MVP/F-TRF-003/US-003-06-mobile-download.md` |
| Become a sender after downloading (growth loop) | `Phase 0-MVP/F-TRF-003/US-003-07-growth-loop.md` |

### F-TRF-004 — Download All (ZIP)
Spec: `Phase 0-MVP/F-TRF-004/F-TRF-004-download-zip.md`

| Story | File |
|---|---|
| Generate a zip of the whole transfer | `Phase 0-MVP/F-TRF-004/US-004-01-generate-zip.md` |
| Get the cached zip instantly on later requests | `Phase 0-MVP/F-TRF-004/US-004-02-cached-zip.md` |
| See why "Download all" is unavailable | `Phase 0-MVP/F-TRF-004/US-004-03-zip-cap.md` |

### F-TRF-005 — Expiry & Auto-Deletion
Spec: `Phase 0-MVP/F-TRF-005/F-TRF-005-expiry-deletion.md`

| Story | File |
|---|---|
| Transfers expire automatically | `Phase 0-MVP/F-TRF-005/US-005-01-auto-expiry.md` |
| Expired storage is deleted after grace | `Phase 0-MVP/F-TRF-005/US-005-02-auto-deletion.md` |
| Download cap ends the transfer early | `Phase 0-MVP/F-TRF-005/US-005-03-download-limit.md` |

### F-TRF-006 — Email Notifications
Spec: `Phase 0-MVP/F-TRF-006/F-TRF-006-email.md`

| Story | File |
|---|---|
| Recipients get a notification email with the link | `Phase 0-MVP/F-TRF-006/US-006-01-recipient-email.md` |
| Failed deliveries retry and dead-letter | `Phase 0-MVP/F-TRF-006/US-006-02-retry-delivery.md` |
| Unsubscribe from a sender's emails | `Phase 0-MVP/F-TRF-006/US-006-03-unsubscribe.md` |
| Reply goes to the sender (branded email) | `Phase 0-MVP/F-TRF-006/US-006-04-reply-branding.md` |
| Receive the email in my language | `Phase 0-MVP/F-TRF-006/US-006-05-localized-email.md` |

### F-TRF-007 — Free-Tier Limits
Spec: `Phase 0-MVP/F-TRF-007/F-TRF-007-limits.md`

| Story | File |
|---|---|
| Use the free tier within its limits | `Phase 0-MVP/F-TRF-007/US-007-01-free-limits.md` |
| Know when my account storage is full | `Phase 0-MVP/F-TRF-007/US-007-02-storage-quota.md` |
| Limits are enforced even if the client lies | `Phase 0-MVP/F-TRF-007/US-007-03-server-enforcement.md` |
| Change limits without a deploy | `Phase 0-MVP/F-TRF-007/US-007-04-flag-limits.md` |

### F-TRF-008 — Accounts (sign-up / sign-in)
Spec: `Phase 0-MVP/F-TRF-008/F-TRF-008-accounts.md`

| Story | File |
|---|---|
| Sign up and sign in with email + password | `Phase 0-MVP/F-TRF-008/US-008-01-signup-password.md` |
| Sign in with a magic link | `Phase 0-MVP/F-TRF-008/US-008-02-magic-link.md` |
| Recover my account when I forgot the password | `Phase 0-MVP/F-TRF-008/US-008-03-forgot-password.md` |
| Manage my profile | `Phase 0-MVP/F-TRF-008/US-008-04-profile.md` |
| See my transfers in My Files | `Phase 0-MVP/F-TRF-008/US-008-05-attribution.md` |
| Delete my account (GDPR) | `Phase 0-MVP/F-TRF-008/US-008-06-delete-account.md` |

### F-TRF-009 — My Files (history)
Spec: `Phase 0-MVP/F-TRF-009/F-TRF-009-my-files.md`

| Story | File |
|---|---|
| Review my transfers | `Phase 0-MVP/F-TRF-009/US-009-01-history-list.md` |
| Filter my transfers | `Phase 0-MVP/F-TRF-009/US-009-02-filters.md` |
| Re-send a transfer from My Files | `Phase 0-MVP/F-TRF-009/US-009-03-resend.md` |
| Delete one of my transfers | `Phase 0-MVP/F-TRF-009/US-009-04-delete.md` |

### F-TRF-010 — Re-send Transfer
Spec: `Phase 0-MVP/F-TRF-010/F-TRF-010-resend.md`

| Story | File |
|---|---|
| Re-send an expired transfer in one click | `Phase 0-MVP/F-TRF-010/US-010-01-resend-click.md` |
| Edit the pre-filled details before re-sending | `Phase 0-MVP/F-TRF-010/US-010-02-edit-prefill.md` |
| Not pay for duplicated storage | `Phase 0-MVP/F-TRF-010/US-010-03-shared-blobs.md` |

### F-TRF-011 — Admin Dashboard (basic)
Spec: `Phase 0-MVP/F-TRF-011/F-TRF-011-admin.md`

| Story | File |
|---|---|
| See platform health at a glance | `Phase 0-MVP/F-TRF-011/US-011-01-overview.md` |
| Find and act on any transfer | `Phase 0-MVP/F-TRF-011/US-011-02-transfers-admin.md` |
| Manage users and plans | `Phase 0-MVP/F-TRF-011/US-011-03-users-admin.md` |
| Edit feature flags and see audit trail | `Phase 0-MVP/F-TRF-011/US-011-04-flags-audit.md` |

### F-TRF-012 — Telemetry & Observability
Spec: `Phase 0-MVP/F-TRF-012/F-TRF-012-telemetry.md`

| Story | File |
|---|---|
| Trace a request end-to-end | `Phase 0-MVP/F-TRF-012/US-012-01-correlation.md` |
| Monitor funnels with dashboards | `Phase 0-MVP/F-TRF-012/US-012-02-dashboards.md` |
| Get alerted before users complain | `Phase 0-MVP/F-TRF-012/US-012-03-alerts.md` |

### F-TRF-013 — Errors, 404s & Degraded States
Spec: `Phase 0-MVP/F-TRF-013/F-TRF-013-errors.md`

| Story | File |
|---|---|
| Understand an error and know what to do | `Phase 0-MVP/F-TRF-013/US-013-01-error-screens.md` |
| Keep using the site when storage is down | `Phase 0-MVP/F-TRF-013/US-013-02-degraded-storage.md` |
| Not lose transfers when email is down | `Phase 0-MVP/F-TRF-013/US-013-03-degraded-email.md` |

### F-TRF-014 — i18n
Spec: `Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md`

| Story | File |
|---|---|
| Use the product in my language | `Phase 0-MVP/F-TRF-014/US-014-01-localized-ui.md` |
| Have the right language automatically | `Phase 0-MVP/F-TRF-014/US-014-02-locale-detection.md` |
| Get emails in my language | `Phase 0-MVP/F-TRF-014/US-014-03-localized-email.md` |

### F-TRF-015 — Dark Mode
Spec: `Phase 0-MVP/F-TRF-015/F-TRF-015-dark-mode.md`

| Story | File |
|---|---|
| Choose light, dark, or system theme | `Phase 0-MVP/F-TRF-015/US-015-01-theme-choice.md` |
| See a consistent theme on every screen | `Phase 0-MVP/F-TRF-015/US-015-02-theme-parity.md` |

### F-TRF-016 — Accessibility
Spec: `Phase 0-MVP/F-TRF-016/F-TRF-016-a11y.md`

| Story | File |
|---|---|
| Complete a transfer without a mouse | `Phase 0-MVP/F-TRF-016/US-016-01-keyboard.md` |
| Hear what's happening (screen reader) | `Phase 0-MVP/F-TRF-016/US-016-02-screen-reader.md` |
| See everything with clear contrast and focus | `Phase 0-MVP/F-TRF-016/US-016-03-contrast-focus.md` |

### F-TRF-017 — Mobile Web
Spec: `Phase 0-MVP/F-TRF-017/F-TRF-017-mobile.md`

| Story | File |
|---|---|
| Send files from my phone | `Phase 0-MVP/F-TRF-017/US-017-01-mobile-upload.md` |
| Download files on my phone | `Phase 0-MVP/F-TRF-017/US-017-02-mobile-download.md` |
| Tap everything comfortably | `Phase 0-MVP/F-TRF-017/US-017-03-touch-ui.md` |

---

---

## Phase 1 — Pro / Monetization (fully specced 2026-08-28)

### F-BIL-001 — Plan Catalog
Spec: `Phase 1-Pro/F-BIL-001/F-BIL-001-plan-catalog.md`

| Story | File |
|---|---|
| Plans are data, not code | `Phase 1-Pro/F-BIL-001/US-018-01-plans-are-data.md` |
| Always resolve my plan's limits | `Phase 1-Pro/F-BIL-001/US-018-02-resolve-plan-limits.md` |
| Plan prices live with the plan | `Phase 1-Pro/F-BIL-001/US-018-03-plan-prices.md` |
| Plan changes apply at my next transfer | `Phase 1-Pro/F-BIL-001/US-018-04-apply-at-finalize.md` |
| Feature availability follows my plan | `Phase 1-Pro/F-BIL-001/US-018-05-plan-feature-toggles.md` |

### F-BIL-002 — Stripe Subscription Lifecycle
Spec: `Phase 1-Pro/F-BIL-002/F-BIL-002-stripe-lifecycle.md`

| Story | File |
|---|---|
| Upgrade to Pro with a few clicks | `Phase 1-Pro/F-BIL-002/US-019-01-upgrade-checkout.md` |
| Manage my subscription myself | `Phase 1-Pro/F-BIL-002/US-019-02-customer-portal.md` |
| Our records mirror Stripe, exactly once | `Phase 1-Pro/F-BIL-002/US-019-03-idempotent-webhooks.md` |
| Get three billing emails before pausing | `Phase 1-Pro/F-BIL-002/US-019-04-billing-emails.md` |
| Keep working during the 14-day grace | `Phase 1-Pro/F-BIL-002/US-019-05-grace-downgrade.md` |
| Be restored when my subscription resumes | `Phase 1-Pro/F-BIL-002/US-019-06-restore-sub.md` |

### F-BIL-003 — Plan Enforcement
Spec: `Phase 1-Pro/F-BIL-003/F-BIL-003-plan-enforcement.md`

| Story | File |
|---|---|
| One place decides my plan | `Phase 1-Pro/F-BIL-003/US-020-01-plan-middleware.md` |
| Keep my live transfers when I downgrade | `Phase 1-Pro/F-BIL-003/US-020-02-overquota-grace.md` |
| See my limits and usage | `Phase 1-Pro/F-BIL-003/US-020-03-plan-screen.md` |
| Feature availability follows my plan | `Phase 1-Pro/F-BIL-003/US-020-04-plan-gates-features.md` |

### F-PRF-001 — Custom "From" Branding
Spec: `Phase 1-Pro/F-PRF-001/F-PRF-001-from-branding.md`

| Story | File |
|---|---|
| Show my organization on my transfers | `Phase 1-Pro/F-PRF-001/US-021-01-org-branding.md` |
| Brand my emails too | `Phase 1-Pro/F-PRF-001/US-021-02-email-branding.md` |
| Keep my own name in front | `Phase 1-Pro/F-PRF-001/US-021-03-personal-name-wins.md` |
| Manage my logo | `Phase 1-Pro/F-PRF-001/US-021-04-manage-logo.md` |

### F-PRF-002 — Scheduled Send
Spec: `Phase 1-Pro/F-PRF-002/F-PRF-002-scheduled-send.md`

| Story | File |
|---|---|
| Pick the moment my files arrive | `Phase 1-Pro/F-PRF-002/US-022-01-schedule-transfer.md` |
| Know exactly what a scheduled send does | `Phase 1-Pro/F-PRF-002/US-022-02-schedule-semantics.md` |
| Schedule within a sane window | `Phase 1-Pro/F-PRF-002/US-022-03-schedule-window.md` |
| Get emails on time, even with a slow backend | `Phase 1-Pro/F-PRF-002/US-022-04-schedule-reliability.md` |

### F-PRF-003 — Download Analytics
Spec: `Phase 1-Pro/F-PRF-003/F-PRF-003-download-analytics.md`

| Story | File |
|---|---|
| See who's downloading my transfer | `Phase 1-Pro/F-PRF-003/US-023-01-analytic-view.md` |
| Know which files mattered | `Phase 1-Pro/F-PRF-003/US-023-02-file-counts.md` |
| Trust that my recipients' data is handled | `Phase 1-Pro/F-PRF-003/US-023-03-privacy-note.md` |
| Have analytics vanish with the transfer | `Phase 1-Pro/F-PRF-003/US-023-04-analytic-lifetime.md` |

### F-PRF-004 — Ads (Free tier)
Spec: `Phase 1-Pro/F-PRF-004/F-PRF-004-ads-free-tier.md`

| Story | File |
|---|---|
| Fund the free tier with one ad | `Phase 1-Pro/F-PRF-004/US-024-01-ad-slot.md` |
| Never see an ad when I'm on Pro | `Phase 1-Pro/F-PRF-004/US-024-02-pro-ad-free.md` |
| Have an ad fail invisibly | `Phase 1-Pro/F-PRF-004/US-024-03-ad-failure.md` |
| Toggle ads without a deploy | `Phase 1-Pro/F-PRF-004/US-024-04-ads-flag.md` |

---

## Phase 2 — Collect, Sign, Albums (fully specced — 44 stories, US-025…US-037)

> **ID mapping (Phase 2):** Phase 2 continues the numbering per feature: F-COL-001 → US-025 · F-COL-002 → 026 · F-COL-003 → 027 · F-COL-004 → 028 · F-COL-005 → 029 · F-SGN-001 → 030 · F-SGN-002 → 031 · F-SGN-003 → 032 · F-SGN-004 → 033 · F-ALB-001 → 034 · F-ALB-002 → 035 · F-ALB-003 → 036 · F-ALB-004 → 037.

### F-COL-001 — Create Collection
Spec: `Phase 2-Collect-Sign-Albums/F-COL-001/F-COL-001-create-collection.md`

| Story | File |
|---|---|
| Create a collection for incoming files | `Phase 2-Collect-Sign-Albums/F-COL-001/US-025-01-create-collection.md` |
| Get a link I can send to people | `Phase 2-Collect-Sign-Albums/F-COL-001/US-025-02-collection-link.md` |
| Decide what each sender must provide | `Phase 2-Collect-Sign-Albums/F-COL-001/US-025-03-field-setup.md` |
| Set a due date when creating | `Phase 2-Collect-Sign-Albums/F-COL-001/US-025-04-create-with-due-date.md` |

### F-COL-002 — Submission
Spec: `Phase 2-Collect-Sign-Albums/F-COL-002/F-COL-002-submission.md`

| Story | File |
|---|---|
| Send my files to a collection without an account | `Phase 2-Collect-Sign-Albums/F-COL-002/US-026-01-submit-as-guest.md` |
| Fill in only what's required | `Phase 2-Collect-Sign-Albums/F-COL-002/US-026-02-fill-required-fields.md` |
| Submit again when I have more files | `Phase 2-Collect-Sign-Albums/F-COL-002/US-026-03-submit-multiple-times.md` |

### F-COL-003 — Submissions Dashboard
Spec: `Phase 2-Collect-Sign-Albums/F-COL-003/F-COL-003-submissions-dashboard.md`

| Story | File |
|---|---|
| Review every submission in one place | `Phase 2-Collect-Sign-Albums/F-COL-003/US-027-01-submissions-overview.md` |
| Download one person's files together | `Phase 2-Collect-Sign-Albums/F-COL-003/US-027-02-download-person-files.md` |
| Accept, decline, or complete a submission | `Phase 2-Collect-Sign-Albums/F-COL-003/US-027-03-set-status.md` |
| Find a specific person's submission | `Phase 2-Collect-Sign-Albums/F-COL-003/US-027-04-find-submission.md` |

### F-COL-004 — Due Date & Grace
Spec: `Phase 2-Collect-Sign-Albums/F-COL-004/F-COL-004-due-date.md`

| Story | File |
|---|---|
| See when my collection is past due | `Phase 2-Collect-Sign-Albums/F-COL-004/US-028-01-past-due-banner.md` |
| Accept late submissions during the grace window | `Phase 2-Collect-Sign-Albums/F-COL-004/US-028-02-late-grace.md` |
| Have my collection close after the grace | `Phase 2-Collect-Sign-Albums/F-COL-004/US-028-03-auto-close.md` |

### F-COL-005 — Completion & Contributor Notification
Spec: `Phase 2-Collect-Sign-Albums/F-COL-005/F-COL-005-completion.md`

| Story | File |
|---|---|
| Mark a submission as done | `Phase 2-Collect-Sign-Albums/F-COL-005/US-029-01-mark-done.md` |
| Let the contributor know when their submission is finished | `Phase 2-Collect-Sign-Albums/F-COL-005/US-029-02-contributor-notified.md` |
| Tell a contributor their submission was declined | `Phase 2-Collect-Sign-Albums/F-COL-005/US-029-03-declined-resubmit.md` |

### F-SGN-001 — Send Document
Spec: `Phase 2-Collect-Sign-Albums/F-SGN-001/F-SGN-001-send-document.md`

| Story | File |
|---|---|
| Send a document for signature | `Phase 2-Collect-Sign-Albums/F-SGN-001/US-030-01-send-document.md` |
| Choose who signs in what order | `Phase 2-Collect-Sign-Albums/F-SGN-001/US-030-02-signer-order.md` |
| Send a Word file (it becomes a PDF) | `Phase 2-Collect-Sign-Albums/F-SGN-001/US-030-03-docx-to-pdf.md` |
| Void a document in flight | `Phase 2-Collect-Sign-Albums/F-SGN-001/US-030-04-void-document.md` |

### F-SGN-002 — Sign
Spec: `Phase 2-Collect-Sign-Albums/F-SGN-002/F-SGN-002-sign.md`

| Story | File |
|---|---|
| Sign by drawing, typing, or uploading | `Phase 2-Collect-Sign-Albums/F-SGN-002/US-031-01-sign-with-3-methods.md` |
| Be sure of the moment I agreed | `Phase 2-Collect-Sign-Albums/F-SGN-002/US-031-02-agreed-on.md` |
| Decline when the document isn't right | `Phase 2-Collect-Sign-Albums/F-SGN-002/US-031-03-decline-document.md` |
| Know whose turn it is | `Phase 2-Collect-Sign-Albums/F-SGN-002/US-031-04-turn-state.md` |

### F-SGN-003 — Audit Trail
Spec: `Phase 2-Collect-Sign-Albums/F-SGN-003/F-SGN-003-audit-trail.md`

| Story | File |
|---|---|
| Prove who signed and when | `Phase 2-Collect-Sign-Albums/F-SGN-003/US-032-01-proof-of-signing.md` |
| Trust that the log can't be quietly edited | `Phase 2-Collect-Sign-Albums/F-SGN-003/US-032-02-immutable-log.md` |
| Know exactly what data we keep | `Phase 2-Collect-Sign-Albums/F-SGN-003/US-032-03-stated-gdpr.md` |

### F-SGN-004 — Final Document
Spec: `Phase 2-Collect-Sign-Albums/F-SGN-004/F-SGN-004-final-document.md`

| Story | File |
|---|---|
| Get the signed document | `Phase 2-Collect-Sign-Albums/F-SGN-004/US-033-01-final-pdf.md` |
| Get the final document without asking | `Phase 2-Collect-Sign-Albums/F-SGN-004/US-033-02-final-notified.md` |
| Trust the final is the real one | `Phase 2-Collect-Sign-Albums/F-SGN-004/US-033-03-final-integrity.md` |

### F-ALB-001 — Create Album
Spec: `Phase 2-Collect-Sign-Albums/F-ALB-001/F-ALB-001-create-album.md`

| Story | File |
|---|---|
| Create a shared album | `Phase 2-Collect-Sign-Albums/F-ALB-001/US-034-01-create-album.md` |
| Protect my album with a password | `Phase 2-Collect-Sign-Albums/F-ALB-001/US-034-02-album-password.md` |
| Cap my album's size | `Phase 2-Collect-Sign-Albums/F-ALB-001/US-034-03-album-size-cap.md` |

### F-ALB-002 — Share Album
Spec: `Phase 2-Collect-Sign-Albums/F-ALB-002/F-ALB-002-share-album.md`

| Story | File |
|---|---|
| Invite people to my album by email | `Phase 2-Collect-Sign-Albums/F-ALB-002/US-035-01-invite-album.md` |
| Share a link that never expires | `Phase 2-Collect-Sign-Albums/F-ALB-002/US-035-02-persistent-link.md` |
| Have an invite that looks like me | `Phase 2-Collect-Sign-Albums/F-ALB-002/US-035-03-invite-branding.md` |

### F-ALB-003 — Contribute to an Album
Spec: `Phase 2-Collect-Sign-Albums/F-ALB-003/F-ALB-003-contribute-album.md`

| Story | File |
|---|---|
| Let someone else add to my album | `Phase 2-Collect-Sign-Albums/F-ALB-003/US-036-01-invite-contributor.md` |
| Browse the album as a grid | `Phase 2-Collect-Sign-Albums/F-ALB-003/US-036-02-grid-view.md` |
| See one image full-bleed | `Phase 2-Collect-Sign-Albums/F-ALB-003/US-036-03-lightbox.md` |
| Close my album to new uploads | `Phase 2-Collect-Sign-Albums/F-ALB-003/US-036-04-close-album.md` |

### F-ALB-004 — Mobile Album View
Spec: `Phase 2-Collect-Sign-Albums/F-ALB-004/F-ALB-004-mobile-album.md`

| Story | File |
|---|---|
| Browse my album on a phone | `Phase 2-Collect-Sign-Albums/F-ALB-004/US-037-01-mobile-grid.md` |
| See a photo full-screen on a phone | `Phase 2-Collect-Sign-Albums/F-ALB-004/US-037-02-mobile-lightbox.md` |
| Download a photo from my phone | `Phase 2-Collect-Sign-Albums/F-ALB-004/US-037-03-mobile-download.md` |

## Phase 3 — Enterprise (fully specced 2026-08-28 — 27 stories, US-038…043)

> **ID mapping (Phase 3):** F-ENT-001 → US-038 · F-ENT-002 → 039 · F-ENT-003 → 040 · F-ENT-004 → 041 · F-ENT-005 → 042 · F-ENT-006 → 043.
> **Scope note:** Phase 3 adds the **org layer** — `Organization` / `OrgMember` / `OrgAuditEntry` (F-ENT-003 owns the DDL; F-ENT-001/002/004/005 extend it). Phase 2 (M5) and Phase 3 (M6) are independent: workspaces and orgs reference the same `AppUser`, but no Phase-2 feature requires an org to exist.

### F-ENT-001 — SSO (Entra ID OIDC + SAML)
Spec: `Phase 3-Enterprise/F-ENT-001/F-ENT-001-sso.md`

| Story | File |
|---|---|
| Connect Entra ID to my org | `Phase 3-Enterprise/F-ENT-001/US-038-01-connect-entra.md` |
| Sign in with my work account | `Phase 3-Enterprise/F-ENT-001/US-038-02-sso-sign-in.md` |
| Add a SAML IdP for hybrid tenants | `Phase 3-Enterprise/F-ENT-001/US-038-03-saml.md` |
| Require SSO for my org | `Phase 3-Enterprise/F-ENT-001/US-038-04-enforce-sso.md` |
| Restrict SSO to an entitled group | `Phase 3-Enterprise/F-ENT-001/US-038-05-group-entitlement.md` |
| Sign out of my work session | `Phase 3-Enterprise/F-ENT-001/US-038-06-sso-logout.md` |

### F-ENT-002 — SCIM User Provisioning
Spec: `Phase 3-Enterprise/F-ENT-002/F-ENT-002-scim.md`

| Story | File |
|---|---|
| Provision users from our IdP automatically | `Phase 3-Enterprise/F-ENT-002/US-039-01-scim-users.md` |
| Keep group membership in sync | `Phase 3-Enterprise/F-ENT-002/US-039-02-scim-groups.md` |
| Turn provisioning off cleanly | `Phase 3-Enterprise/F-ENT-002/US-039-03-scim-secret.md` |

### F-ENT-003 — Organization Admin & Audit
Spec: `Phase 3-Enterprise/F-ENT-003/F-ENT-003-org-admin.md`

| Story | File |
|---|---|
| Create our organization | `Phase 3-Enterprise/F-ENT-003/US-040-01-create-org.md` |
| Invite my team and manage their roles | `Phase 3-Enterprise/F-ENT-003/US-040-02-manage-members.md` |
| Prove what happened in our org | `Phase 3-Enterprise/F-ENT-003/US-040-03-audit-log.md` |
| Apply the org plan to my team's transfers | `Phase 3-Enterprise/F-ENT-003/US-040-04-org-plan.md` |

### F-ENT-004 — Data Residency
Spec: `Phase 3-Enterprise/F-ENT-004/F-ENT-004-data-residency.md`

| Story | File |
|---|---|
| Choose where our data lives | `Phase 3-Enterprise/F-ENT-004/US-041-01-choose-region.md` |
| Verify our files are in the right place | `Phase 3-Enterprise/F-ENT-004/US-041-02-residency-report.md` |
| Trust the check runs without me | `Phase 3-Enterprise/F-ENT-004/US-041-03-nightly-check.md` |

### F-ENT-005 — Custom Subdomain
Spec: `Phase 3-Enterprise/F-ENT-005/F-ENT-005-custom-subdomain.md`

| Story | File |
|---|---|
| Put our links on our own domain | `Phase 3-Enterprise/F-ENT-005/US-042-01-claim-domain.md` |
| Know exactly what DNS to set | `Phase 3-Enterprise/F-ENT-005/US-042-02-verify-dns.md` |
| Have recipients land on our domain | `Phase 3-Enterprise/F-ENT-005/US-042-03-canonical-links.md` |

### F-ENT-006 — Workspaces
Spec: `Phase 3-Enterprise/F-ENT-006/F-ENT-006-workspaces.md`

| Story | File |
|---|---|
| Split the team into workspaces | `Phase 3-Enterprise/F-ENT-006/US-043-01-create-workspace.md` |
| Give a workspace its own plan and limits | `Phase 3-Enterprise/F-ENT-006/US-043-02-workspace-limits.md` |
| Send as my team, not as me | `Phase 3-Enterprise/F-ENT-006/US-043-03-send-from-workspace.md` |
| See each team's usage separately | `Phase 3-Enterprise/F-ENT-006/US-043-04-workspace-usage.md` |

---

## Phase X — Cross-Cutting (fully specced 2026-08-28)

> Sprinkled across all phases; each feature is gated by its `feature.*` flag (F-XCT-001) and inherits the Definition of Done. Story numbers continue from Phase 3: US-044…048.

### F-XCT-001 — Feature Flags & Remote Limits
Spec: `Phase X-Cross-Cutting/F-XCT-001/F-XCT-001-feature-flags.md`

| Story | File |
|---|---|
| Change a limit without a deploy | `Phase X-Cross-Cutting/F-XCT-001/US-044-01-edit-limits.md` |
| Gate a phase feature with a flag | `Phase X-Cross-Cutting/F-XCT-001/US-044-02-gate-feature.md` |

### F-XCT-002 — Search (Transfers)
Spec: `Phase X-Cross-Cutting/F-XCT-002/F-XCT-002-search.md`

| Story | File |
|---|---|
| Find a transfer by file name or recipient | `Phase X-Cross-Cutting/F-XCT-002/US-045-01-find-transfer.md` |

### F-XCT-003 — Email Settings (Notification Preferences)
Spec: `Phase X-Cross-Cutting/F-XCT-003/F-XCT-003-email-settings.md`

| Story | File |
|---|---|
| Turn off my notification emails | `Phase X-Cross-Cutting/F-XCT-003/US-046-01-turn-off.md` |
| Trust that recipients are unaffected | `Phase X-Cross-Cutting/F-XCT-003/US-046-02-recipients-unaffected.md` |

### F-XCT-004 — GDPR: Data Export & Erasure Completeness
Spec: `Phase X-Cross-Cutting/F-XCT-004/F-XCT-004-gdpr-export-erasure.md`

| Story | File |
|---|---|
| Download all my data | `Phase X-Cross-Cutting/F-XCT-004/US-047-01-download-my-data.md` |
| My telemetry is erased too | `Phase X-Cross-Cutting/F-XCT-004/US-047-02-erase-telemetry.md` |
| Prove the erasure (DPO report) | `Phase X-Cross-Cutting/F-XCT-004/US-047-03-erasure-report.md` |

### F-XCT-005 — PII Policy (emails, sender names, telemetry)
Spec: `Phase X-Cross-Cutting/F-XCT-005/F-XCT-005-pii-policy.md`

| Story | File |
|---|---|
| My file name is anonymous in telemetry | `Phase X-Cross-Cutting/F-XCT-005/US-048-01-anonymous-telemetry.md` |
| Catch a PII leak before release | `Phase X-Cross-Cutting/F-XCT-005/US-048-02-ci-gate.md` |
| Cite the PII list (DPO) | `Phase X-Cross-Cutting/F-XCT-005/US-048-03-cite-policy.md` |
