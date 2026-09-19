# T-036 — Get a unique link for my files

**Story:** US-002-01 | **Feature:** F-TRF-002 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-002/US-002-01-unique-link.md`
**Coarse task (Milestone-Backlog.md):** T-010, T-013
**Status:** pending

---

## Scope

As a sender, I want a short, clean link I can paste anywhere, without a URL bar full of tracking noise, so that I can share my files in any channel (chat, email, document).

**Actor:** Guest or signed-in user who has finished uploading (F-TRF-001).

**Goal:** Receive a permanent, unguessable link that uniquely identifies the transfer.

Happy path:

1. `FinalizeTransferCommand` runs (idempotency-keyed): server-side blob copy, `Transfer(Status=0)` + `FileItem` + `BlobRef(RefCount=1)` created.
2. The transfer receives an 8-char Crockford base32 `LinkId` (no `0/O/1/I`).
3. The link screen shows the full public URL `{origin}/t/{linkId}` in a copy field with a **Copy** button.
4. `GET /t/{linkId}` resolves (recipient page) as soon as the transfer is sent.
5. The link contains no query parameters, no tracking, no account hints.

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

## Exit check

- [ ] Scenario 1: a draft with 3 uploaded files
- [ ] Scenario 2: the link is active
- [ ] Scenario 3: I double-press "Send." (double finalize)
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-002/US-002-01-unique-link.md`
- Feature: `TRF-002-transfer-link.md` (FR-002-1, FR-002-2, EC-002-1)
- Plan AC: AC-002-1, AC-002-4
- Architecture: TA-3.2, TA-4.1.5, TA-4.2#2, TA-7.1
- Design: UI-Reference §4.4, §5.2
- Milestone: T-010, T-013
