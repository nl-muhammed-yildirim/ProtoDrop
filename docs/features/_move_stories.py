import os, glob, sys

base = r"C:/Users/myild/source/repos/ProtoDrop/docs/features/Phase 0-MVP"
for feat in ("F-TRF-008", "F-TRF-009", "F-TRF-010"):
    d = os.path.join(base, feat)
    for f in sorted(glob.glob(os.path.join(d, "US-*.md"))):
        base_name = os.path.basename(f)[:-3]
        target_dir = os.path.join(d, base_name)
        os.makedirs(target_dir, exist_ok=True)
        os.rename(f, os.path.join(target_dir, os.path.basename(f)))
    print(feat, sorted(os.listdir(d)))
