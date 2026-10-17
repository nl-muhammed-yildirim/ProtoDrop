import os
base = r'C:\Users\myild\source\repos\ProtoDrop\docs\features\Phase 0-Foundation\F-FND-007'
os.rename(os.path.join(base, 'US-055-03-canonical-paths', 'T-055-02-iblobstore-port.md'),
          os.path.join(base, 'US-055-02-round-trip', 'T-055-02-iblobstore-port.md'))
print('moved')
