# Per-Story Task Files — Phase 0 (MVP)

One task file per user story, in feature order. **T-031…T-096 = 66 stories (US-001-01…US-017-03).**

**Numbering scheme:** T-001…T-030 keep their `Milestone-Backlog.md` numbers (coarse M0–M4 tasks). These per-story tasks fill T-031…T-096. The old backlog rows T-031…T-069 (Phases 1/2/3/X) are renumbered **T-097…T-135** (+66) in `Milestone-Backlog.md` and across docs.

Each file: scope (story, actor, goal, happy path), verbatim acceptance criteria, edge cases, a measurable exit checklist (all scenarios + closed lists + AGENT.md §4 gate), and links (story, feature, plan ACs, TA, UI-Reference, coarse task).

## Index

| Task | Story | Feature | Title | Coarse task |
|---|---|---|---|---|
| T-031 | US-001-01 | F-TRF-001 | Select files with drag & drop, click, or paste | T-012 |
| T-032 | US-001-02 | F-TRF-001 | Stage and remove multiple files | T-012 |
| T-033 | US-001-03 | F-TRF-001 | Get warned before upload exceeds the limit | T-009 (server check), T-012 (client check) |
| T-034 | US-001-04 | F-TRF-001 | Watch per-file and overall progress | T-012 |
| T-035 | US-001-05 | F-TRF-001 | Recover from a failed upload | T-012 |
| T-036 | US-002-01 | F-TRF-002 | Get a unique link for my files | T-010, T-013 |
| T-037 | US-002-02 | F-TRF-002 | Send the transfer to recipients by email | T-011, T-013 |
| T-038 | US-002-03 | F-TRF-002 | Protect my transfer with a password | T-011, T-015 |
| T-039 | US-002-04 | F-TRF-002 | Add sender info and a note | T-011, T-013 |
| T-040 | US-002-05 | F-TRF-002 | Create a link-only transfer (no emails) | T-011, T-013 |
| T-041 | US-003-01 | F-TRF-003 | Open a transfer link with no account | T-014 |
| T-042 | US-003-02 | F-TRF-003 | Download all files in one zip | T-016, T-017 |
| T-043 | US-003-03 | F-TRF-003 | Download individual files | T-016 |
| T-044 | US-003-04 | F-TRF-003 | Unlock a password-protected transfer | T-015 |
| T-045 | US-003-05 | F-TRF-003 | See remaining downloads and expired states | T-014 |
| T-046 | US-003-06 | F-TRF-003 | Download from a phone | T-014, T-027 |
| T-047 | US-003-07 | F-TRF-003 | Become a sender after downloading (growth loop) | T-014 |
| T-048 | US-004-01 | F-TRF-004 | Generate a zip of the whole transfer | T-017 |
| T-049 | US-004-02 | F-TRF-004 | Get the cached zip instantly on later requests | T-017 |
| T-050 | US-004-03 | F-TRF-004 | See why "Download all" is unavailable | T-017 |
| T-051 | US-005-01 | F-TRF-005 | Transfers expire automatically | T-018 |
| T-052 | US-005-02 | F-TRF-005 | Expired storage is deleted after grace | T-018 |
| T-053 | US-005-03 | F-TRF-005 | Download cap ends the transfer early | T-016, T-018 |
| T-054 | US-006-01 | F-TRF-006 | Recipients get a notification email with the link | T-019 |
| T-055 | US-006-02 | F-TRF-006 | Failed deliveries retry and dead-letter | T-019 |
| T-056 | US-006-03 | F-TRF-006 | Unsubscribe from a sender's emails | T-019 |
| T-057 | US-006-04 | F-TRF-006 | Reply goes to the sender (branded email) | T-019 |
| T-058 | US-006-05 | F-TRF-006 | Receive the email in my language | T-019, T-026 |
| T-059 | US-007-01 | F-TRF-007 | Use the free tier within its limits | T-020 |
| T-060 | US-007-02 | F-TRF-007 | Know when my account storage is full | T-020 |
| T-061 | US-007-03 | F-TRF-007 | Limits are enforced even if the client lies | T-009, T-020 |
| T-062 | US-007-04 | F-TRF-007 | Change limits without a deploy | T-005, T-024 |
| T-063 | US-008-01 | F-TRF-008 | Sign up and sign in with email + password | T-021 |
| T-064 | US-008-02 | F-TRF-008 | Sign in with a magic link | T-021 |
| T-065 | US-008-03 | F-TRF-008 | Recover my account when I forgot the password | T-021 |
| T-066 | US-008-04 | F-TRF-008 | Manage my profile | T-021 |
| T-067 | US-008-05 | F-TRF-008 | See my transfers in My Files | T-021, T-022 |
| T-068 | US-008-06 | F-TRF-008 | Delete my account (GDPR) | T-021 |
| T-069 | US-009-01 | F-TRF-009 | Review my transfers | T-022 |
| T-070 | US-009-02 | F-TRF-009 | Filter my transfers | T-022 |
| T-071 | US-009-03 | F-TRF-009 | Re-send a transfer from My Files | T-023 |
| T-072 | US-009-04 | F-TRF-009 | Delete one of my transfers | T-022 |
| T-073 | US-010-01 | F-TRF-010 | Re-send an expired transfer in one click | T-023 |
| T-074 | US-010-02 | F-TRF-010 | Edit the pre-filled details before re-sending | T-023 |
| T-075 | US-010-03 | F-TRF-010 | Not pay for duplicated storage | T-023 |
| T-076 | US-011-01 | F-TRF-011 | See platform health at a glance | T-024 |
| T-077 | US-011-02 | F-TRF-011 | Find and act on any transfer | T-024 |
| T-078 | US-011-03 | F-TRF-011 | Manage users and plans | T-024 |
| T-079 | US-011-04 | F-TRF-011 | Edit feature flags and see the audit trail | T-024 |
| T-080 | US-012-01 | F-TRF-012 | Trace a request end-to-end | T-008, T-025 |
| T-081 | US-012-02 | F-TRF-012 | Monitor funnels with dashboards | T-025 |
| T-082 | US-012-03 | F-TRF-012 | Get alerted before users complain | T-025 |
| T-083 | US-013-01 | F-TRF-013 | Understand an error and know what to do | T-025 |
| T-084 | US-013-02 | F-TRF-013 | Keep using the site when storage is down | T-025 |
| T-085 | US-013-03 | F-TRF-013 | Not lose transfers when email is down | T-025 (health surface), T-019 (delivery machinery) |
| T-086 | US-014-01 | F-TRF-014 | Use the product in my language | T-026 |
| T-087 | US-014-02 | F-TRF-014 | Have the right language automatically | T-026 |
| T-088 | US-014-03 | F-TRF-014 | Get emails in my language | T-019 (template machinery), T-026 (locale set) |
| T-089 | US-015-01 | F-TRF-015 | Choose light, dark, or system theme | T-027 |
| T-090 | US-015-02 | F-TRF-015 | See a consistent theme on every screen | T-027 |
| T-091 | US-016-01 | F-TRF-016 | Complete a transfer without a mouse | T-027 |
| T-092 | US-016-02 | F-TRF-016 | Hear what's happening (screen reader) | T-027 |
| T-093 | US-016-03 | F-TRF-016 | See everything with clear contrast and focus | T-027 |
| T-094 | US-017-01 | F-TRF-017 | Send files from my phone | T-027 |
| T-095 | US-017-02 | F-TRF-017 | Download files on my phone | T-027 |
| T-096 | US-017-03 | F-TRF-017 | Tap everything comfortably | T-027 |
