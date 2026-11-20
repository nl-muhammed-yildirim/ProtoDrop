#!/usr/bin/env python3
"""Ready-column watchdog (cron monitor — runs every minute, no LLM).

Full paginated board scan of the @ProtoDrop Projects v2 board + a claimed-state
file so each task wakes the orchestrator exactly ONCE per lifecycle. The cron
monitor compares this script's stdout byte-for-byte between ticks and only
wakes the agent when it changes, so stability is the contract:

- New item in the Ready column -> full READY block printed (issue REST for the
  plan path), entry added to state. Output CHANGES -> wake #1 (the task start).
- While claimed (Ready or In progress inside STUCK_STALE_MINUTES) the SAME
  block is re-emitted every tick: constant output -> no double-wake while a run
  is in flight, and the Ready -> In progress column move by the orchestrator is
  invisible to the monitor.
- Still in Ready after STALE_READY_MINUTES -> STILL READY line appended (a
  previous run died before claiming it — self-heal).
- Claimed item stuck at 'In progress' past STUCK_STALE_MINUTES -> STUCK IN
  PROGRESS line (a run likely died between the status change and commit+PR;
  verify git state / resume or reset to Backlog).
- Item moves to Done or Backlog while claimed (human action after review) ->
  entry dropped, block disappears from output -> exactly one final wake
  (completion signal: verify the PR + mark task done). 'In review' is NOT
  terminal — the block keeps re-emitting so setting In review causes no extra
  fire.

Wake lines carry no wall-clock or per-minute counters (only fixed thresholds),
so output is deterministic between ticks. State lives in .hermes/board-watch-
state.json inside the repo (gitignored): {issue_number: {"claimed_at": iso,
"block": [lines...]}} — survives scratch pruning; `release <n>` drops an entry
manually, `show` dumps it.

Usage:
    python scripts/board-watch.py            # watchdog tick (cron monitor)
    python scripts/board-watch.py release N  # drop issue N from state
    python scripts/board-watch.py show       # dump state file
"""

import datetime
import json
import os
import subprocess
import sys
import urllib.request

GH = "C:/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe"
BOARD_ID = "PVT_kwHOC4R8ic4BsTl4"  # user project #2 '@ProtoDrop'
REPO = "nl-muhammed-yildirim/ProtoDrop"
STALE_READY_MINUTES = 30      # still in Ready column -> previous run died early
STUCK_STALE_MINUTES = 720     # left Ready, no progress for 12h -> stuck mid-task

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATE_FILE = os.environ.get("PROTO_BOARD_STATE") or os.path.join(
    REPO_ROOT, ".hermes", "board-watch-state.json")


def gh_token() -> str:
    out = subprocess.run([GH, "auth", "token"], capture_output=True, text=True).stdout.strip()
    if not out:
        raise RuntimeError("gh auth token returned empty — is the CLI logged in?")
    return out


def gql(tok: str, query: str):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query}).encode(),
        headers={"Authorization": f"Bearer {tok}", "User-Agent": "board-watch"},
    )
    return json.load(urllib.request.urlopen(req))


def rest(tok: str, path: str):
    req = urllib.request.Request(
        f"https://api.github.com{path}",
        headers={"Authorization": f"Bearer {tok}", "User-Agent": "board-watch",
                 "Accept": "application/vnd.github+json"},
    )
    return json.load(urllib.request.urlopen(req))


def find_plan_path(body: str):
    """First docs/plans/... path referenced in the issue body, or None."""
    for line in (body or "").splitlines():
        tokens = [t.strip().strip("`") for t in line.replace("`", " ").split()]
        for tok in tokens:
            if tok.startswith("docs/plans/") and tok.endswith(".md"):
                return tok
    for line in (body or "").splitlines():
        for tok in [t.strip().strip("`") for t in line.replace("`", " ").split()]:
            if tok.endswith("-plan.md"):
                return f"docs/plans/{tok}"
    return None


def scan_board(tok: str):
    """Full paginated scan; returns {issue_number: (title, url, status_name)}."""
    by_num = {}
    after = None
    while True:
        q = ("""{ node(id: %s) { ... on ProjectV2 { items(first: 100, after: %s) {
          pageInfo { hasNextPage endCursor }
          nodes { id type
            status: fieldValueByName(name: "Status") { __typename
              ... on ProjectV2ItemFieldSingleSelectValue { name } }
            content { __typename ... on Issue { number title url } } } } } } }""") % (
            json.dumps(BOARD_ID), json.dumps(after))
        page = gql(tok, q)["data"]["node"]["items"]
        for n in page["nodes"]:
            c = n.get("content") or {}
            if c.get("__typename") == "Issue":
                by_num[c["number"]] = (c["title"], c.get("url", ""),
                                      (n.get("status") or {}).get("name"))
        if not page["pageInfo"]["hasNextPage"]:
            break
        after = page["pageInfo"]["endCursor"]
    return by_num


def load_state() -> dict:
    try:
        with open(STATE_FILE, encoding="utf-8") as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def save_state(state: dict) -> None:
    os.makedirs(os.path.dirname(STATE_FILE), exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2)


def render_block(num, title, url, milestone, plan):
    return [
        f"READY #{num}: {title}",
        f"  milestone : {milestone}",
        f"  plan file : {plan or 'NOT FOUND — check issue body'}",
        f"  url       : {url}",
    ]


def main() -> None:
    args = sys.argv[1:]

    if args and args[0] == "show":
        print(json.dumps(load_state(), indent=2))
        return
    if len(args) == 2 and args[0] == "release":
        state = load_state()
        removed = state.pop(args[1], None)
        save_state(state)
        print(f"released {args[1]}" if removed else f"{args[1]} not in state")
        return

    tok = gh_token()
    board = scan_board(tok)
    state = load_state()
    now = datetime.datetime.now(datetime.timezone.utc)
    out: list[str] = []

    for num, (title, url, status) in sorted(board.items()):
        key = str(num)
        entry = state.get(key)
        if isinstance(entry, str):  # v1 format (bare iso string) — drop so it re-claims fresh
            del state[key]
            entry = None

        claimed_at = entry.get("claimed_at") if entry else None
        age_min = int((now - datetime.datetime.fromisoformat(
            claimed_at)).total_seconds() // 60) if claimed_at else None

        if status == "Ready":
            if entry is None:
                issue = rest(tok, f"/repos/{REPO}/issues/{num}")
                block = render_block(
                    num, title, url,
                    (issue.get("milestone") or {}).get("title"),
                    find_plan_path(issue.get("body") or ""))
                state[key] = {"claimed_at": now.isoformat(), "block": block}
            else:
                block = entry["block"]  # saved lines -> byte-stable across ticks
            out.extend(block)
            if age_min is not None and age_min >= STALE_READY_MINUTES:
                out.append(
                    f"STILL READY #{num}: in the Ready column for "
                    f"{STALE_READY_MINUTES}+ minutes; a previous run likely died before claiming it")

        elif status in ("In progress", "In review"):
            # Re-emit the identical saved block so monitor output stays constant
            # through Ready -> In progress -> In review transitions (no double-
            # wake while a run is in flight). Unclaimed items are someone else's
            # work — don't claim. STUCK only applies to 'In progress' (death
            # between status change and commit+PR); by 'In review' the code is
            # already committed + PR'd per the workflow, so no stuck check there.
            if entry is None:
                continue
            out.extend(entry["block"])
            if status == "In progress" and age_min is not None and age_min >= STUCK_STALE_MINUTES:
                out.append(
                    f"STUCK IN PROGRESS #{num}: left the Ready column "
                    f"{STUCK_STALE_MINUTES}+ minutes ago with no commit+PR; a run likely died — "
                    f"verify git state, resume or reset to Backlog.")

        elif status in ("Done", "Backlog"):
            # Terminal from our perspective -> drop the claim. The block
            # disappearing from output is exactly one final wake (completion:
            # verify PR + mark task done).
            if entry is not None:
                del state[key]

    save_state(state)
    print("\n".join(out))


if __name__ == "__main__":
    main()
