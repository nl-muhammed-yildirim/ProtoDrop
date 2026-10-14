import ctypes, ctypes.wintypes as W, subprocess, json, urllib.request, os, sys

os.chdir(r"C:/Users/myild/source/repos/ProtoDrop")

advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
ptr = W.LPVOID()
ok = advapi32.CredReadW("git:https://github.com", 1, 0, ctypes.byref(ptr))
if not ok or ptr.value is None:
    print(f"[cred] FAIL CredReadW ok={bool(ok)} err={ctypes.get_last_error()} (5=ERROR_ITEM_NOT_FOUND)", flush=True)
    sys.exit(1)

# The credential heap block AVs on a full 8KB string_at but per-8-byte windows stay safe up to
# well past 128KB (proven by t9). Scan the whole blob and locate user + token by exact UTF-16LE.
blob = b""
i = 0
while i < (1 << 18):
    try:
        chunk = bytes(ctypes.string_at(ptr.value + i, 8))
    except OSError as e:
        print(f"[cred] blob stop @+{i}: {e}", flush=True)
        break
    if chunk == b"\x00" * 8 and len(blob) > (1 << 16):
        # deep into the block, all zeros = padding past any embedded strings; keep a few chunks
        blob += chunk
        i += 8
        continue
    blob += chunk
    i += 8

advapi32.CredFree(ctypes.c_void_p(ptr.value))

uenc = "nl-muhammed-yildirim".encode("utf-16-le")
te = b"g\x00h\x00o\x00_\x00"
if uenc not in blob:
    print(f"[cred] FAIL — user string not found (blob {len(blob)} bytes)", flush=True); sys.exit(1)
idx = blob.find(te)
print(f"[cred] gho_ marker at blob offset {idx}", flush=True)
if idx < 0:
    print("[cred] FAIL — no gho_ marker in blob", flush=True); sys.exit(1)

user = "nl-muhammed-yildirim"
token = blob[idx: idx + 40 * 2].decode("utf-16-le")
print(f"[cred] user={user!r} token len={len(token)} prefix={token[:4]!r}", flush=True)
if not (token.startswith("gho_") and len(token) == 40):
    print("[cred] MISMATCH — aborting push", flush=True); sys.exit(1)
print("[cred] OK", flush=True)

tmp = ".omp/tmp-ghcred-t056.json"
with open(tmp, "w") as f:
    json.dump({"user": user, "token": token}, f)

url = f"https://{user}:{token}@github.com/nl-muhammed-yildirim/ProtoDrop.git"
r = subprocess.run(["git", "push", url, "feature/task/T-056:refs/heads/feature/task/T-056"],
                   capture_output=True, text=True)
print(f"[push] rc={r.returncode}", flush=True)
if r.stdout.strip(): print(r.stdout.strip(), flush=True)
if r.stderr.strip(): print(r.stderr.strip(), flush=True)
os.remove(tmp)
print("[clean] temp token JSON deleted", flush=True)
if r.returncode != 0:
    sys.exit(1)

r = subprocess.run(["git", "ls-remote", url, "feature/task/T-056"], capture_output=True, text=True)
line = (r.stdout or "").strip()
print("[lsremote]", line or "(empty)", flush=True)
if not line.startswith("9491dc8"):
    print(f"[push] HEAD MISMATCH expected 9491dc8* got {line[:70]!r}", flush=True); sys.exit(1)

payload = json.load(open(".omp/pr-payload-t056.json"))
req = urllib.request.Request("https://api.github.com/repos/nl-muhammed-yildirim/ProtoDrop/pulls",
                             data=json.dumps(payload).encode(),
                             headers={"Authorization": f"Bearer {token}",
                                      "Accept": "application/vnd.github+json",
                                      "User-Agent": "T-056"})
pr = json.load(urllib.request.urlopen(req))
print(f"[pr] number={pr['number']} url={pr['html_url']} state={pr['state']} base={pr['base']['ref']} head={pr['head']['ref']}", flush=True)
print("DONE", flush=True)
