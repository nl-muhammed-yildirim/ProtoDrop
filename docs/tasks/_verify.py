import glob, os, re, sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
root = r"C:\Users\myild\source\repos\ProtoDrop\docs\tasks"
fs = sorted(glob.glob(os.path.join(root, "T-*.md")))
print(len(fs), "task files")
problems = 0
for f in fs:
    t = open(f, encoding="utf-8").read()
    lines = t.splitlines()
    for i, ln in enumerate(lines[:14]):
        if ln.startswith("** "):
            problems += 1
            print("STRAY", os.path.basename(f), i + 1, repr(ln))
    # sanity: has scope, gherkin block, exit check
    for marker in ("## Scope", "```gherkin", "## Exit check", "## Links"):
        if marker not in t:
            problems += 1
            print("MISSING", marker, os.path.basename(f))
print("problems:", problems)
