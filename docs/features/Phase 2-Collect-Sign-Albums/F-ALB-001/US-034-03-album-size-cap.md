# US-034-03 — Cap my album's size

**Feature:** F-ALB-001 — Create Album | **Status:** pending

---

**Story:** As an album owner, I want to set a max size for my album, so that one album can't eat my whole storage quota and I can reject oversize uploads before they happen.
**Actor:** album owner.
**Goal:** an optional per-album `MaxSizeBytes` (≤ the owner's plan `STORAGE_QUOTA`), enforced at upload, with the album's total counting against the owner's storage meter (FR-034-5).

## Preconditions

- An album exists (US-034-01); the owner's plan quota is known (`PlanContext`, F-BIL-001).

## Happy path

1. Creation with a cap: `MaxSizeBytes` stored (validated ≤ plan quota; a cap above the plan is clamped with a note).
2. Uploads: the draft/finalize path (F-ALB-003) checks `album.UsedBytes + incoming > MaxSizeBytes` → rejection with the cap named (AC-034-4, before the upload, client pre-check + server finalize check).
3. The My Albums row shows `used / cap` (e.g., "1.2 GB of 2 GB").

## Alternative flows

- **No cap set**: the owner's plan quota is the effective limit (the default, FR-034-1) — the same meter, F-TRF-007-4 semantics.
- **Plan quota hit first** (album under its cap, plan full): the plan error wins — "Your storage is full" (F-TRF-007-2), not the album cap.

## Acceptance criteria

```gherkin
Given an album capped at 2 GB with 1.9 GB used
When an upload of 200 MB is attempted
Then it is rejected with the cap named, before any block is uploaded

Given an album without a cap
When its owner's plan quota fills
Then the plan-level limit is the one enforced
```

## Edge cases

- Cap set *after* items exist: allowed if `UsedBytes ≤ new cap`; otherwise inline "album already has {used}" (MVP: set at creation only, documented — the cap field is at creation, FR-034-1).
- Contributor uploads count against the cap (the cap is on the album, not on the uploader, F-ALB-003).

## UI notes

- Cap field: "Max size (optional)" input with a "Your plan: {quota}" hint; default empty = plan quota.
- Row: `used / cap` or just `used` (no cap) — `--fs-small`, `--fg-muted`.

## Technical notes

- `Album.MaxSizeBytes BIGINT NULL` (F-ALB-001 DDL).
- Enforcement: `Album.UsedBytes` maintained on item add/remove (same transaction as `AlbumItem` insert — F-ALB-003); client pre-check reads it from the draft response (F-TRF-007 client/server split).
- Storage meter: album uploads increment the owner's `STORAGE_QUOTA` usage exactly like transfers (F-TRF-007-4) — one meter, three surfaces (transfers, collections, albums).

## Links

- Feature: `ALB-001-create-album.md` (FR-034-1/5, AC-034-4)
- Related: F-TRF-007 (the quota system), F-ALB-003 (uploads that hit the cap), US-020-03 (the plan screen shows the same quota)
