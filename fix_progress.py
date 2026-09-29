import os

# Go up two levels from src/wa.web to repo root
os.chdir(os.path.join(os.getcwd(), '..', '..'))

with open('docs/PROGRESS.md', 'r') as f:
    lines = f.readlines()

# Fix 1: arithmetic typo — web suite has 23 tests (7+6+9+1), not 22
lines[136] = lines[136].replace('web 22 + new 3', 'web 23 + new 3')

# Fix 2: update quoted commit subject for 919cb3c
old = 'latest revert point `919cb3c` ("revert to foundation") — the first US-001 web attempt (old T-03x numbering) was reverted and redone under current backlog numbering as **T-012**.'
new = 'latest commit `919cb3c` — feat: T-012 web part 1 — US-001-01 select files (drop/picker/paste) + US-001-02 stage/remove; toast/upload-engine foundation'
lines[74] = lines[74].replace(old, new)

with open('docs/PROGRESS.md', 'w') as f:
    f.writelines(lines)

print("Fixed PROGRESS.md")