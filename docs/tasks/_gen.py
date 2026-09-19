# -*- coding: utf-8 -*-
"""Generate one task file per Phase 0 user story (T-031..T-096) into docs/tasks/.

Numbering (agreed in the 'Phase Zero Tasks' session):
- T-001..T-030 keep their numbers in Milestone-Backlog.md.
- T-031..T-096 = 66 per-story Phase 0 tasks, feature order (US-001-01 first).
- Old T-031..T-069 (Phases 1/X) shift to T-097..T-135 (separate edit).
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

FEAT_ROOT = r"C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-MVP"
OUT = r"C:\Users\myild\source\repos\ProtoDrop\docs\tasks"
START = 31


def collect():
    rows = []
    for d in sorted(os.listdir(FEAT_ROOT)):
        dp = os.path.join(FEAT_ROOT, d)
        if not os.path.isdir(dp):
            continue
        for fn in sorted(os.listdir(dp)):
            m = re.match(r"US-(\d+)-(\d+)-(.+)\.md", fn)
            if m:
                us_id = "US-%s-%s" % (m.group(1), m.group(2))
                rows.append((d, fn, us_id))
    return rows


def split_sections(lines):
    sec = {}
    cur = None
    buf = []
    for ln in lines:
        if ln.startswith("## "):
            if cur is not None:
                sec[cur] = buf
            cur = ln[3:].strip()
            buf = []
        elif cur is not None:
            buf.append(ln)
    if cur is not None:
        sec[cur] = buf
    return sec


def field(pre, name):
    pat = re.compile(r"\*\*%s:\*\*\s*(.*)" % re.escape(name))
    for ln in pre:
        m = pat.match(ln.strip())
        if m:
            return m.group(1).strip()
    return ""


def parse(path):
    text = open(path, encoding="utf-8").read()
    lines = text.splitlines()
    pre = []
    for ln in lines:
        if ln.startswith("## "):
            break
        pre.append(ln)
    h1 = lines[0]
    title = h1.split("\u2014", 1)[1].strip() if "\u2014" in h1 else h1
    story = field(pre, "Story")
    goal = field(pre, "Goal")
    actor = field(pre, "Actor")

    sec = split_sections(lines)

    hp = []
    for ln in sec.get("Happy path", []):
        mm = re.match(r"\s*\d+\.\s+(.*)", ln)
        if mm:
            hp.append(mm.group(1).strip())

    gherkin = []
    in_g = False
    for ln in sec.get("Acceptance criteria", []):
        s = ln.strip()
        if s.startswith("```gherkin"):
            in_g = True
            continue
        if in_g and s.startswith("```"):
            break
        if in_g:
            gherkin.append(ln)
    while gherkin and not gherkin[-1].strip():
        gherkin.pop()
    gherkin_text = "\n".join(gherkin)
    given_lines = re.findall(r"^\s*Given\s+(.*)$", gherkin_text, re.M)

    edges = []
    for ln in sec.get("Edge cases", []):
        if ln.strip().startswith("- "):
            edges.append(ln.strip()[2:].strip())

    links = []
    for ln in sec.get("Links", []):
        if ln.strip().startswith("- "):
            links.append(ln.strip()[2:].strip())

    coarse = ""
    for lk in links:
        if lk.startswith("Milestone:"):
            coarse = lk.split(":", 1)[1].strip()
    return dict(title=title, story=story, goal=goal, actor=actor,
                hp=hp, gherkin=gherkin_text, givens=given_lines,
                edges=edges, links=links, coarse=coarse)


def generate():
    rows = collect()
    files = []
    for i, (d, fn, us_id) in enumerate(rows):
        n = START + i
        t_id = "T-%03d" % n
        p = parse(os.path.join(FEAT_ROOT, d, fn))
        stem = re.sub(r"\.md$", "", fn).lower()
        out_name = "%s-%s.md" % (t_id, stem)

        scenarios = p["givens"]
        sc_lines = []
        for j, g in enumerate(scenarios, 1):
            sc_lines.append("- [ ] Scenario %d: %s" % (j, g))

        parts = []
        parts.append("# %s \u2014 %s" % (t_id, p["title"]))
        parts.append("")
        parts.append("**Story:** %s | **Feature:** %s | **Phase:** 0 \u2014 MVP" % (us_id, d))
        parts.append("**Story file:** `../features/Phase 0-MVP/%s/%s`" % (d, fn))
        parts.append("**Coarse task (Milestone-Backlog.md):** %s" % (p["coarse"] or "TBD"))
        parts.append("**Status:** pending")
        parts.append("")
        parts.append("---")
        parts.append("")
        parts.append("## Scope")
        parts.append("")
        parts.append(p["story"])
        parts.append("")
        parts.append("**Actor:** %s" % p["actor"])
        parts.append("")
        parts.append("**Goal:** %s" % p["goal"])
        parts.append("")
        parts.append("Happy path:")
        parts.append("")
        for k, step in enumerate(p["hp"], 1):
            parts.append("%d. %s" % (k, step))
        parts.append("")
        parts.append("## Acceptance criteria")
        parts.append("")
        parts.append("```gherkin")
        parts.append(p["gherkin"])
        parts.append("```")
        parts.append("")
        if p["edges"]:
            parts.append("## Edge cases")
            parts.append("")
            for e in p["edges"]:
                parts.append("- %s" % e)
            parts.append("")
        parts.append("## Exit check")
        parts.append("")
        parts.extend(sc_lines)
        parts.append("- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)")
        parts.append("- [ ] No new NuGet/npm package without an ADR line (golden rule 1)")
        parts.append("- [ ] AGENT.md \u00a74 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)")
        parts.append("")
        parts.append("## Links")
        parts.append("")
        parts.append("- Story: `../features/Phase 0-MVP/%s/%s`" % (d, fn))
        for lk in p["links"]:
            parts.append("- %s" % lk)
        parts.append("")
        with open(os.path.join(OUT, out_name), "w", encoding="utf-8") as f:
            f.write("\n".join(parts))
        files.append((t_id, us_id, d, p["title"], p["coarse"], out_name))
    return files


def write_index(files):
    by_feat = {}
    for t_id, us_id, d, title, coarse, out_name in files:
        by_feat.setdefault(d, []).append((t_id, us_id, title, coarse, out_name))
    L = []
    L.append("# Per-Story Task Files \u2014 Phase 0 (MVP)")
    L.append("")
    L.append("One task file per user story, in feature order. **T-031\u2026T-096 = 66 stories (US-001-01\u2026US-017-03).**")
    L.append("")
    L.append("**Numbering scheme:** T-001\u2026T-030 keep their `Milestone-Backlog.md` numbers (coarse M0\u2013M4 tasks). These per-story tasks fill T-031\u2026T-096. The old backlog rows T-031\u2026T-069 (Phases 1/2/3/X) are renumbered **T-097\u2026T-135** (+66) in `Milestone-Backlog.md` and across docs.")
    L.append("")
    L.append("Each file: scope (story, actor, goal, happy path), verbatim acceptance criteria, edge cases, a measurable exit checklist (all scenarios + closed lists + AGENT.md \u00a74 gate), and links (story, feature, plan ACs, TA, UI-Reference, coarse task).")
    L.append("")
    L.append("## Index")
    L.append("")
    L.append("| Task | Story | Feature | Title | Coarse task |")
    L.append("|---|---|---|---|---|")
    for d in sorted(by_feat):
        for t_id, us_id, title, coarse, out_name in by_feat[d]:
            L.append("| %s | %s | %s | %s | %s |" % (t_id, us_id, d, title, coarse))
    with open(os.path.join(OUT, "README.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(L) + "\n")


def main():
    files = generate()
    write_index(files)
    print("wrote %d task files + README.md" % len(files))
    for t_id, us_id, d, title, coarse, out_name in files:
        print("%s %s %s coarse=%s" % (t_id, us_id, d, coarse or "?"))


if __name__ == "__main__":
    main()
