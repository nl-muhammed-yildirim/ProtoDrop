import os, io

base = r"C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-Foundation"

# old filename -> new filename (per folder)
renames = {
    "F-FND-001": [
        ("US-050-01-open-and-build.md", "US-049-01-open-and-build.md"),
        ("US-050-02-layer-rules.md", "US-049-02-layer-rules.md"),
    ],
    "F-FND-002": [
        ("US-051-01-docker-local.md", "US-050-01-docker-local.md"),
        ("US-051-02-health-check.md", "US-050-02-health-check.md"),
        ("US-051-03-serilog-console.md", "US-050-03-serilog-console.md"),
    ],
    "F-FND-003": [
        ("US-052-01-pr-gate.md", "US-051-01-pr-gate.md"),
        ("US-052-02-format-determinism.md", "US-051-02-format-determinism.md"),
        ("US-052-03-image-build.md", "US-051-03-image-build.md"),
    ],
    "F-FND-004": [
        ("US-053-01-migrate-fresh-db.md", "US-052-01-migrate-fresh-db.md"),
        ("US-053-02-seed-everywhere.md", "US-052-02-seed-everywhere.md"),
    ],
}

for folder, pairs in renames.items():
    fdir = os.path.join(base, folder)
    for old, new in pairs:
        src = os.path.join(fdir, old)
        dst = os.path.join(fdir, new)
        if os.path.exists(src):
            os.rename(src, dst)
            print("renamed:", new)
        elif not os.path.exists(dst):
            print("MISSING:", old)

print("done")
