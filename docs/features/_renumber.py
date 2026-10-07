import os, io

base = r"C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-Foundation"

rules = {
    "F-FND-001": [("US-050", "US-049"), ("FR-050", "FR-049"), ("AC-050", "AC-049"), ("EC-050", "EC-049")],
    "F-FND-002": [("US-051", "US-050"), ("FR-051", "FR-050"), ("AC-051", "AC-050"), ("EC-051", "EC-050")],
    "F-FND-003": [("US-052", "US-051"), ("FR-052", "FR-051"), ("AC-052", "AC-051"), ("EC-052", "EC-051")],
    "F-FND-004": [("US-053", "US-052"), ("FR-053", "FR-052"), ("AC-053", "AC-052"), ("EC-053", "EC-052")],
    "F-FND-005": [("US-054", "US-053"), ("FR-054", "FR-053"), ("AC-054", "AC-053"), ("EC-054", "EC-053")],
}

for folder, reps in rules.items():
    fdir = os.path.join(base, folder)
    for name in sorted(os.listdir(fdir)):
        p = os.path.join(fdir, name)
        if not os.path.isfile(p):
            continue
        with io.open(p, "r", encoding="utf-8") as f:
            text = f.read()
        orig = text
        for old, new in reps:
            text = text.replace(old, new)
        if folder == "F-FND-004":
            # cross-folder reference to F-FND-002's docker story (not covered by this folder's rules)
            text = text.replace("US-051-01", "US-050-01")
        if text != orig:
            with io.open(p, "w", encoding="utf-8", newline="") as f:
                f.write(text)
            print("updated:", name)

print("done")
