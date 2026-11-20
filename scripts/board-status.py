#!/usr/bin/env python3
"""Read or change a ProtoDrop board item's **Status** column (Projects v2 GraphQL).

The dev process (see AGENTS.md): Claude Code is Architect + Reviewer, Hermes is
Developer. A task in `Ready` gets moved to `In progress` before the Coder run and
to `In review` when coding completes:

    python scripts/board-status.py set 679 "In progress"   # before Coder
    python scripts/board-status.py set 679 "In review"     # after Coder PASS
    python scripts/board-status.py get 679                 # verify current status
    python scripts/board-status.py list                    # non-Done items by column

Exact option names (case-sensitive, from the board's Status field):
Backlog | Ready | In progress | In review | Done

GraphQL notes (verified against api.github.com schema, Nov 2026):
- mutation: updateProjectV2ItemFieldValue(input: UpdateProjectV2ItemFieldValueInput!)
  -> payload field is `projectV2Item` (NOT projectItem).
- value shape for a single-select field: { singleSelectOptionId: <option id> } —
  the option IDs below, NOT the option name.

Auth: gh CLI login at C:/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe
(its token has the required `project` scope — the GCM/GitHub-App token does NOT).
"""

import json
import subprocess
import sys
import urllib.request

GH = "C:/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe"
BOARD_ID = "PVT_kwHOC4R8ic4BsTl4"  # user project #2 '@ProtoDrop' (527 items)
STATUS_FIELD_ID = "PVTSSF_lAHOC4R8ic4BsTl4zhqiVpg"
VALID_STATUSES = ("Backlog", "Ready", "In progress", "In review", "Done")

# Status option IDs (singleSelectOptionId values — not the names).
STATUS_OPTION_IDS = {
    "Backlog": "f75ad846",
    "Ready": "61e4505c",
    "In progress": "47fc9ee4",
    "In review": "df73e18b",
    "Done": "98236657",
}


def gh_token() -> str:
    out = subprocess.run([GH, "auth", "token"], capture_output=True, text=True).stdout.strip()
    if not out:
        raise RuntimeError("gh auth token returned empty — is the CLI logged in?")
    return out


def gql(tok: str, query: str, variables=None):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query, "variables": variables or {}}).encode(),
        headers={"Authorization": f"Bearer {tok}", "User-Agent": "board-status"},
    )
    return json.load(urllib.request.urlopen(req))


def scan_items(tok: str):
    """Return the full item list (paginated) as a flat list of node dicts."""
    nodes = []
    after = None
    while True:
        q = (
            "{ node(id: %s) { ... on ProjectV2 { items(first: 100, after: %s) {"
            "pageInfo { hasNextPage endCursor } nodes { id type"
            " status: fieldValueByName(name: \"Status\") { __typename"
            "... on ProjectV2ItemFieldSingleSelectValue { name } }"
            " content { __typename ... on Issue { number title url }"
            " ... on PullRequest { number title url } } } } } } }"
        ) % (json.dumps(BOARD_ID), json.dumps(after))
        data = gql(tok, q)["data"]["node"]
        page = data["items"]
        nodes.extend(page["nodes"])
        if not page["pageInfo"]["hasNextPage"]:
            break
        after = page["pageInfo"]["endCursor"]
    return nodes


def find_item(nodes, issue_number: int):
    for n in nodes:
        c = n.get("content") or {}
        if (c.get("__typename") == "Issue" and c.get("number") == issue_number) \
                or (c.get("__typename") == "PullRequest" and c.get("number") == issue_number):
            return n
    return None


def cmd_get(tok: str, number: int):
    item = find_item(scan_items(tok), number)
    if not item:
        print(f"issue #{number} is not on the board")
        sys.exit(1)
    status = (item.get("status") or {}).get("name", "?")
    c = item["content"]
    label = "PR" if c["__typename"] == "PullRequest" else "issue"
    print(f"#{number} ({label}) [{c['title']}] -> {status}")


def cmd_set(tok: str, number: int, status: str):
    if status not in VALID_STATUSES:
        sys.exit(f"invalid status {status!r}; valid: {' | '.join(VALID_STATUSES)}")
    item = find_item(scan_items(tok), number)
    if not item:
        print(f"issue #{number} is not on the board")
        sys.exit(1)
    m = ("mutation($input: UpdateProjectV2ItemFieldValueInput!) {"
         " updateProjectV2ItemFieldValue(input: $input) { projectV2Item { id } } }")
    resp = gql(tok, m, {"input": {
        "projectId": BOARD_ID,
        "itemId": item["id"],
        "fieldId": STATUS_FIELD_ID,
        "value": {"singleSelectOptionId": STATUS_OPTION_IDS[status]},
    }})
    if "errors" in resp and resp["errors"]:
        sys.exit(json.dumps(resp["errors"], indent=2))
    # Verify by re-reading.
    after = find_item(scan_items(tok), number)
    new_status = (after.get("status") or {}).get("name") if after else "?"
    print(f"#{number} -> {new_status}")
    if new_status != status:
        sys.exit(f"expected {status!r}, board shows {new_status!r}")


def cmd_list(tok: str):
    nodes = scan_items(tok)
    by_col = {}
    for n in nodes:
        col = (n.get("status") or {}).get("name", "?")
        c = n.get("content") or {}
        t = c.get("__typename")
        if t == "Issue":
            by_col.setdefault(col, []).append(f"#{c['number']} {c['title']}")
        elif t == "PullRequest":
            by_col.setdefault(col, []).append(f"PR #{c['number']} {c['title']}")
    for col in VALID_STATUSES:
        rows = by_col.get(col) or []
        print(f"{col} ({len(rows)}):")
        for r in rows[:25]:
            print("   ", r)


def main() -> None:
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    tok = gh_token()
    cmd = args[0]
    if cmd == "get" and len(args) == 2:
        cmd_get(tok, int(args[1]))
    elif cmd == "set" and len(args) == 3:
        cmd_set(tok, int(args[1]), args[2])
    elif cmd == "list":
        cmd_list(tok)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
