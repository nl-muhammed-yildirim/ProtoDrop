import os

root = r"C:\Users\myild\source\repos\ProtoDrop\docs\features"
for r, d, fs in os.walk(root):
    for f in sorted(fs):
        print(os.path.join(r, f))
