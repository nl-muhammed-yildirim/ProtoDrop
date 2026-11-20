#!/usr/bin/env python3
"""List ProtoDrop GitHub Project (board) items in the **Ready** status column.

The dev process (see AGENTS.md): Claude Code is Architect + Reviewer, Hermes is
Developer. A task enters `Ready` when its plan file exists under docs/plans/.
This script prints Ready items (one block per item); empty stdout = nothing ready.

Auth: uses the gh CLI login at C:/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe
(its token has the required `project` scope — the GCM/GitHub-App token does NOT).

Usage: python scripts/board-ready.py [--json]
"""

import json
import subprocess
import sys
import urllib.request

GH = "C:/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe"
BOARD_ID = "PVT_kwHOC4R8ic4BsTl4"  # user project #2 '@ProtoDrop' (527 items)
REPO = "nl-muhammed-yildirim/ProtoDrop"


def gh_token() -> str:
    out = subprocess.run([GH, "auth", "token"], capture_output=True, text=True).stdout.strip()
    if not out:
        raise RuntimeError("gh auth token returned empty — is the CLI logged in?")
    return out


def gql(tok: str, query: str):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query}).encode(),
        headers={"Authorization": f"Bearer {tok}", "User-Agent": "board-ready"},
    )
    return json.load(urllib.request.urlopen(req))


def rest(tok: str, path: str):
    req = urllib.request.Request(
        f"https://api.github.com{path}",
        headers={"Authorization": f"Bearer {tok}", "User-Agent": "board-ready",
                 "Accept": "application/vnd.github+json"},
    )
    return json.load(urllib.request.urlopen(req))


def find_plan_path(body: str):
    """Return the first docs/plans/... path referenced in the issue body, or None."""
    for line in (body or "").splitlines():
        tokens = [t.strip().strip("`") for t in line.replace("`", " ").split()]
        for tok in tokens:
            if tok.startswith("docs/plans/") and tok.endswith(".md"):
                return tok
    # Fallback: a bare <T>-plan.md filename that lives under docs/plans/
    for line in (body or "").splitlines():
        for tok in [t.strip().strip("`") for t in line.replace("`", " ").split()]:
            if tok.endswith("-plan.md"):
                return f"docs/plans/{tok}"
    return None


def main() -> None:
    as_json = "--json" in sys.argv[1:]
    tok = gh_token()

    q = """{ node(id: %s) { ... on ProjectV2 { items(first: 100) { nodes { id type
      status: fieldValueByName(name: "Status") { __typename
        ... on ProjectV2ItemFieldSingleSelectValue { name } }
      content { __typename ... on Issue { number title url } } } } } } }""" % json.dumps(BOARD_ID)

    data = gql(tok, q)["data"]["node"]
    ready = [
        n for n in data["items"]["nodes"]
        if (n.get("status") or {}).get("name") == "Ready"
        and (n.get("content") or {}).get("__typename") == "Issue"
    ]

    items = []
    for n in ready:
        c = n["content"]
        issue = rest(tok, f"/repos/{REPO}/issues/{c['number']}")
        body = issue.get("body") or ""
        plan = find_plan_path(body)
        items.append({
            "issue": c["number"],
            "title": c["title"],
            "url": c.get("url"),
            "milestone": (issue.get("milestone") or {}).get("title"),
            "labels": [l["name"] for l in issue.get("labels", [])],
            "plan_file": plan,
            "body_preview": body[:600],
        })

    if as_json:
        print(json.dumps(items, indent=2))
        return

    if not items:
        return  # empty stdout = nothing ready (watchdog convention)

    for it in items:
        print(f"READY #{it['issue']}: {it['title']}")
        print(f"  milestone : {it['milestone']}")
        print(f"  plan file : {it['plan_file'] or 'NOT FOUND — check issue body'}")
        print(f"  url       : {it['url']}")
        print(f"  spec      : {it['body_preview'].strip().splitlines()[0] if it['body_preview'] else ''}")
        print()


if __name__ == "__main__":
    main()
