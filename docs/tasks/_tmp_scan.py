import re, os
root = r"C:\Users\myild\source\repos\ProtoDrop"
pat = re.compile(r"T-\d{3}")
out = []
for dirpath, dirnames, filenames in os.walk(root):
    dirnames[:] = [d for d in dirnames if d not in ("node_modules", "bin", "obj")]
    for fn in filenames:
        if not fn.endswith(".md"):
            continue
        p = os.path.join(dirpath, fn)
        text = open(p, encoding="utf-8").read()
        hits = sorted(set(pat.findall(text)), key=lambda t: int(t[1:]))
        if hits:
            out.append((os.path.relpath(p, root), hits))
for rel, hits in sorted(out):
    print(rel, "->", ", ".join(hits))
