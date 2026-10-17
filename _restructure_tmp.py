import os, re, glob

root = r'C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-Foundation'
pat = re.compile(r'`((US-0[456]\d-\d\d-[a-z]+(?:-[a-z]+)*)\.md)`')
changed = []
for md in glob.glob(os.path.join(root, 'F-FND-*', 'F-FND-*.md')):
    with open(md, encoding='utf-8') as f:
        c = f.read()
    def repl(m):
        name = m.group(1)[:-3]
        return '`' + name + '/' + name + '.md`'
    nc = pat.sub(repl, c)
    if nc != c:
        with open(md, 'w', encoding='utf-8') as f:
            f.write(nc)
        changed.append(os.path.basename(md))

for x in changed:
    print(x)
print('changed:', len(changed))
