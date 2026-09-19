import os, re
root = r"C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-MVP"
rows = []
for d in sorted(os.listdir(root)):
    dp = os.path.join(root, d)
    if not os.path.isdir(dp):
        continue
    for fn in sorted(os.listdir(dp)):
        m = re.match(r"US-(\d+)-(\d+)-(.+)\.md", fn)
        if m:
            rows.append((d, fn, m.group(1), m.group(2), m.group(3)))
# print table grouped by feature (d)
cur = None
for d, fn, u1, u2, slug in rows:
    if d != cur:
        cur = d
        print("\n== " + d)
    print("  " + fn, "US-" + u1 + "-" + u2)
