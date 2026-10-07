import sys
for p in sys.argv[1:]:
    p = p.strip('"')
    print("=" * 20, p)
    with open(p, encoding="utf-8") as fh:
        print(fh.read())
