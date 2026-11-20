#!/usr/bin/env python3
"""Ready-column watchdog (cron monitor — runs every minute, no LLM).

Full paginated board scan of the @ProtoDrop Projects v2 board + a claimed-state
file so each task wakes the orchestrator exactly once per state change:

- New item in the Ready column -> full READY block printed (issue REST for the
  plan path), entry added to state. The monitor wakes the agent on this tick.
- Same item still in Ready after STALE_READY_MINUTES -> STILL READY line
  (previous run likely died before claiming it — self-heal, no manual reset).
- Claimed item that left Ready but is stuck at 'In progress' for more than
  STUCK_STALE_MINUTES -> STUCK IN PROGRESS line (a run likely died between the
  status change and commit+PR; verify git state / resume or reset to Backlog).
- Entry whose issue moved on (Done/Backlog) or vanished from the board -> entry
  dropped, so if it re-enters Ready later it fires as a fresh READY block.

Claimed items are otherwise QUIET: no line is printed while they sit in Ready
or In progress under their claim window, so column moves by the orchestrator do
NOT change this script's output between ticks (no double-wake). Wakes happen
only on: new Ready item, STILL READY, or STUCK IN PROGRESS.

Output is deterministic between ticks (elapsed times rounded to whole minutes,
no wall-clock), which the monitor requires: unchanged output = agent skipped.

State lives in .hermes/board-watch-state.json inside the repo (gitignored) so it
survives scratch pruning; `release <n>` drops an entry manually, `show` dumps it.

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
    lines = []

    for num, (title, url, status) in sorted(board.items()):
        key = str(num)
        if status == "Ready":
            if key not in state:
                issue = rest(tok, f"/repos/{REPO}/issues/{num}")
                body = issue.get("body") or ""
                plan = find_plan_path(body)
                milestone = (issue.get("milestone") or {}).get("title")
                lines += [
                    f"READY #{num}: {title}",
                    f"  milestone : {milestone}",
                    f"  plan file : {plan or 'NOT FOUND — check issue body'}",
                    f"  url       : {url}",
                    "",
                ]
                state[key] = now.isoformat()
            else:
                age_min = int((now - datetime.datetime.fromisoformat(
                    state[key])).total_seconds() // 60)
                if age_min >= STALE_READY_MINUTES:
                    lines.append(
                        f"STILL READY #{num}: {title} — in the Ready column for "
                        f"{age_min} minutes; a previous run likely died before claiming it")
        elif status == "In progress":
            # Claimed entries stay quiet while in flight (no re-wake on column
            # moves); only staleness surfaces. Unclaimed In-progress items are
            # someone else's work — don't touch them.
            if key not in state:
                continue
            age_min = int((now - datetime.datetime.fromisoformat(
                state[key])).total_seconds() // 60)
            if age_min >= STUCK_STALE_MINUTES:
                lines.append(
                    f"STUCK IN PROGRESS #{num}: {title} — left the Ready column "
                    f"{age_min} minutes ago but is still 'In progress'; a run likely "
                    f"died before commit+PR. Verify git state, resume or reset to Backlog.")
        elif status in ("Done", "Backlog"):
            if key in state:
                del state[key]  # moved on -> drop the claim so a future Ready re-entry re-fires

    save_state(state)
    print("\n".join(lines))


if __name__ == "__main__":
    main()
