import os
base = r"C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-MVP"
for root, dirs, files in os.walk(base):
    rel = os.path.relpath(root, base)
    if rel == '.':
        rel = ''
    for f in sorted(files):
        print(os.path.join(rel, f).replace('\\', '/'))
