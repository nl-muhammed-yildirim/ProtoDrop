# US-051-02 — Catch format drift between Windows and Linux

**Feature:** F-FND-003 — CI Pipeline (PR Gate) | **Status:** done (T-003, 2026-08-31)

---

**Story:** As a developer on Windows building for a Linux CI runner, I want line endings and formatting to be locked in the repo, so that `dotnet format --verify-no-changes` passes identically everywhere.
**Actor:** Developer (any OS), CI pipeline (Linux runners).
**Goal:** Format checks are deterministic: same file, same verdict on Windows local and Linux CI.

## Preconditions

- `.gitattributes` present at repo root (T-003).
- `lint-dotnet` job running `dotnet format src/wa.slnx --verify-no-changes`.

## Happy path

1. Developer edits C# files on Windows (CRLF locally is fine — git normalizes).
2. PR opened; lint-dotnet runs on Linux.
3. The check passes because `.gitattributes` normalized line endings at checkout time for both sides.

## Alternative flows

- **Drift introduced:** a file checked in with mixed endings → verify-no-changes fails locally and in CI the same way (same rule, same verdict).

## Acceptance criteria

```gherkin
Given .gitattributes locks line endings repo-wide
When I commit C# files on Windows and run ci.yml on Linux
Then dotnet format --verify-no-changes passes without spurious line-ending failures

Given a file with mixed line endings checked in
When lint-dotnet runs (locally or in CI)
Then the failure message is identical on both platforms
```

## Edge cases

- `.editorconfig` not yet landed: `dotnet format --verify-no-changes` is a structural no-op at M0 (T-003 note); it becomes meaningful when the editorconfig arrives — the line-ending lock works regardless.
- Web files: eslint owns web formatting; prettier step gated until its dev-dep lands (EC-051-1).

## UI notes

- None.

## Technical notes

- `.gitattributes` is the T-003 deliverable that makes cross-platform format checks possible — it predates any editorconfig.
- Keep the rule list minimal: text normalization + binary markers only.

## Links

- Feature: `F-FND-003-ci-pipeline.md` (FR-051-3, AC-051-4)
- Architecture: TA-12.1
- Milestone: T-003
