# T-051-01 — Add .gitattributes locking line endings

**Story:** US-051-02 | **Spec:** FR-051-3, AC-051-4 | **Size:** S (one file)
**Depends on:** T-049-04 (solution builds — files to normalize exist)

---

## Context to read (only these)

- `../../F-FND-003-ci-pipeline.md` → FR-051-3 + Technical notes (`.gitattributes` line only)
- `US-051-02-format-determinism.md` → happy path + Edge cases

## Instructions

1. Add `.gitattributes` at repo root with a **minimal** rule list: text normalization (`* text=auto eol=lf`) + binary markers (images, fonts, node_modules binaries as needed).
2. Re-stage existing files so the normalization applies (`git add --renormalize .`).

## Exit check

- [x] `dotnet format src/wa.slnx --verify-no-changes` passes locally on Windows
- [x] A file checked in with mixed line endings fails identically locally and (later) in CI — same rule, same verdict

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, solution at src/wa.slnx).
Task T-051-01 — lock line endings for cross-platform format checks.
Read first (only): docs/features/Phase 0-Foundation/F-FND-003/F-FND-003-ci-pipeline.md (FR-051-3 + the .gitattributes Technical-notes line) and US-051-02-format-determinism.md (happy path + Edge cases).
Do exactly:
1. Create .gitattributes at repo root with a minimal rule list: * text=auto eol=lf plus binary markers only — no per-language overrides.
2. Run git add --renormalize . so existing files pick up the normalization.
3. Verify dotnet format src/wa.slnx --verify-no-changes passes on this Windows machine (note: it is a structural no-op until an .editorconfig lands — T-003 note; the line-ending lock works regardless).
Done when: C# files edited on Windows pass verify-no-changes with no spurious line-ending failures, and the rule list stays minimal.
Constraints: text normalization + binary markers only (US-051-02 Technical notes); do not add an .editorconfig in this task.
```
