#!/usr/bin/env python3
"""verify_plan.py — mechanical drift check between a T-xxx plan and live repo source.

Usage:  scripts/verify_plan.py [--done] [docs/plans/T-*.md]
        (no plan argument: verifies the newest plan in docs/plans/)
        --done: the task is already implemented — CREATE files existing on disk is expected.

Checks performed:
  1. FILES FOR CODER table — CREATE files must not exist yet, MODIFY files must exist.
  2. Every backticked repo path anywhere in the plan exists; `path:N` line refs are in range
     (CREATE rows exempt for existence, but their line refs still checked).
  3. Closed sets — every cited ProblemCode member / telemetry event name / CSS custom property
     must exist in its live source file. Cited set counts ("18 members", "23 events + ...") must
     match the actual count (REPO-FACTS.md is the reference, but truth is the source).
  4. Storage keys — any quoted localStorage/cookie key cited must match the constants in source.

Exit code: 0 clean, 1 drift found, 2 plan not parseable.

Design note: this runs INSIDE the architect spawn, so a non-zero exit means "fix the plan and
re-run" — it is cheap enough to loop on (pure file reads + greps, < 1 s)."""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def fail(msg: str) -> None:
    print(f"FAIL  {msg}")


# ---------------------------------------------------------------- section readers

def read(path: str | Path) -> str | None:
    p = Path(path) if Path(path).is_absolute() else REPO / path
    return p.read_text(encoding="utf-8", errors="replace") if p.is_file() else None


def closed_set_members(src_path: Path, const_re: str) -> list[str]:
    """Const member names from a C# static class file (name = value where they differ)."""
    text = read(src_path) or ""
    return re.findall(const_re, text)


def css_tokens(index_css: str) -> set[str]:
    root_match = re.search(r":root\s*\{(.*?)\n\s*\}", index_css, re.S)
    block = root_match.group(1) if root_match else index_css
    return set(re.findall(r"^\s*(--[a-z][a-z0-9-]*)", block, re.M))


# ---------------------------------------------------------------- plan parsing

def files_for_coder(plan: str) -> list[tuple[str, str]]:
    """Rows of the FILES FOR CODER table -> [(file, action)] (case-insensitive header)."""
    m = re.search(r"###\s*FILES\s+FOR\s+CODER(.*?)(?=^#{2,3}\s|\Z)", plan, re.M | re.S)
    if not m:
        return []
    rows = []
    for line in m.group(1).splitlines():
        cells = [c.strip() for c in line.split("|")]
        if len(cells) < 3:
            continue
        action_cell = next((c.upper() for c in cells if c.upper() in ("CREATE", "MODIFY")), None)
        path_re = re.search(r"`((?:src|tests|docs|scripts)/[^`]+)`", line)
        if not path_re or not action_cell:
            continue  # header / separator rows
        rows.append((path_re.group(1), action_cell))
    return rows


def cited_paths(plan: str) -> list[tuple[str, int | None]]:
    """All backticked repo paths (with optional trailing :LINE refs), deduped in order."""
    out = []
    for m in re.finditer(r"`((?:src|tests|docs|scripts)/[^`]+)`", plan):
        raw = m.group(1).strip()
        pm = re.match(r"^([A-Za-z0-9_./{}-]+)(?::(\d+))?$", raw)
        if not pm:
            continue  # prose inside backticks, not a path reference
        out.append((pm.group(1), int(pm.group(2)) if pm.group(2) else None))
    return list(dict.fromkeys(out))


# ---------------------------------------------------------------- checks

def main() -> int:
    argv = sys.argv[1:]
    done = "--done" in argv          # task already implemented → CREATE files exist on purpose
    args = [a for a in argv if not a.startswith("--")]
    candidates = sorted(REPO.glob("docs/plans/T-*.md"), reverse=True)
    if not candidates:
        fail("no plan files found under docs/plans/")
        return 2
    target = Path(args[0]) if args else candidates[0]
    plan = read(target.relative_to(REPO)) if target.is_relative_to(REPO) else \
        (target.read_text(encoding="utf-8") if target.is_file() else None)
    if plan is None:
        fail(f"plan not found: {target}")
        return 2

    errors = warnings = 0
    print(f"== verify_plan.py — {target.name} ==")

    # -- 1. FILES FOR CODER existence -------------------------------------------
    rows = files_for_coder(plan)
    if not rows:
        fail("no parseable FILES FOR CODER table found")
        return 2
    for path, action in rows:
        p = REPO / path
        exists = p.is_file()
        if action == "CREATE" and exists and not done:
            errors += 1; fail(f"{path}: plan says CREATE but file already exists (stale? coder must not clobber)")
        elif action == "MODIFY" and not exists:
            errors += 1; fail(f"{path}: plan says MODIFY but file does not exist")

    # -- 2. cited paths + line refs ---------------------------------------------
    for base, line in cited_paths(plan):
        p = REPO / base
        if not p.is_file():
            if base.startswith(("src/", "tests/")):
                errors += 1; fail(f"cited path missing (stale — drift?): {base}")
            else:
                warnings += 1; print(f"warn  cited doc missing: {base} (advisory)")
        elif line and int(line) > max(p.read_text(encoding="utf-8", errors="replace").count("\n"), 0):
            warnings += 1; print(f"warn  cited line out of range: {base}:{line}")

    # -- 3. closed sets ----------------------------------------------------------
    problem = closed_set_members(
        REPO / "src/wa.domain/Errors/ProblemCodes.cs", r'public const string \w+ = "(\w+)";')
    telemetry_events = [e for e in closed_set_members(
        REPO / "src/wa.domain/Telemetry/TelemetryEvents.cs", r'= "([a-z][a-z0-9_.{}]*)";')]
    css = css_tokens(read("src/wa.web/src/index.css") or "")

    # Backticked ALL_CAPS tokens (ProblemCode wire names etc.): must be in the live closed set.
    http_methods = {"GET", "POST", "PATCH", "DELETE", "OPTIONS"}
    cited_codes = set(re.findall(r"`([A-Z][A-Z_]{3,})`", plan))
    for code in sorted(cited_codes):
        if code not in problem and code not in http_methods:
            errors += 1; fail(f"cited ProblemCode {code} not in the live closed set ({len(problem)} members)")

    # Dotted telemetry names (email.template_fallback style — the riskiest citations).
    file_exts = {"css", "ts", "tsx", "js", "cs", "md", "json", "html", "scss"}
    known_dotted = {"wa.session"}  # cookie name — stable, defined in SessionMiddleware
    for ev in sorted(set(re.findall(r"`([a-z][a-z0-9]*(?:\.[a-z0-9_]+)+)`", plan))):
        if ev.startswith("wa."):       # project namespaces (wa.web, wa.domain…) or the cookie
            continue
        if ev not in telemetry_events and ev.rsplit(".", 1)[-1] not in file_exts \
           and ev not in known_dotted:
            errors += 1; fail(f"cited telemetry name {ev!r} not an exact member of TelemetryEvents")

    # CSS tokens must exist in :root — unless every mention is negated ("no --fs-h2 token").
    for token in sorted(set(re.findall(r"--[a-z][a-z0-9-]*", plan))):
        if token not in css:
            mentions = [m.start() for m in re.finditer(re.escape(token), plan)]
            positive = any(not re.search(r"\b(no|never)\s+", plan[max(0, s - 8):s]) for s in mentions)
            if positive:
                errors += 1; fail(f"cited CSS token {token} missing from index.css :root")

    # Count claims like "18 members" near ProblemCodes must match the live count.
    for m in re.finditer(r"(\d+)\s+members\b", plan):
        ctx = plan[max(0, m.start() - 200):m.end()].lower()
        if ("problemcode" in ctx or "closed set" in ctx) and int(m.group(1)) != len(problem):
            errors += 1; fail(f"plan claims {m.group(1)} members — live ProblemCodes count is {len(problem)}")

    # -- 4. storage keys ----------------------------------------------------------
    session_src = read("src/wa.web/src/core/auth/session.ts") or ""
    live_keys = set(re.findall(r"'(protoDrop\.[a-z0-9]+)'", session_src))
    for key in sorted(set(re.findall(r"'(protoDrop\.[a-z0-9]+)'", plan))):
        if live_keys and key not in live_keys:
            errors += 1; fail(f"cited client storage key {key!r} doesn't match the constant in session.ts")

    print(f"== result: {'DRIFT' if errors else 'CLEAN'} ({errors} error(s), {warnings} warning(s)) ==")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
