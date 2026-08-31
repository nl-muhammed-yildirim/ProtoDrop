# US-041-01 — Choose where our data lives

**Feature:** F-ENT-004 — Data Residency | **Status:** pending

---

**Story:** As the buyer creating our org, I want to pick the data region, so that "where is our data?" has a one-sentence answer.
**Actor:** Buyer (creating the org — the region selector lives on the F-ENT-003 create screen).
**Goal:** one decision, made once, shown forever.

## Preconditions

- Create-org screen (US-040-01).

## Happy path

1. Region selector: two radio cards — "European Union (westeurope)" / "United States (eastus2)", each with a one-line plain-language description ("Data stored in the European Union").
2. Create → `Organization.Region` saved; default `westeurope` if untouched.
3. All org-attributed uploads from that moment route to the regional blob account (FR-041-2).

## Alternative flows

- **Later change:** `PATCH /orgs/{id}` with a region → `409 RESIDENCY_IMMUTABLE` (Phase 3 rule, FR-041-3); the settings screen says "Region can be changed with a migration — contact us."

## Acceptance criteria

```gherkin
Given I create an org and select eastus2
Then the org's region is eastus2
And org members' uploads land in the eastus2 storage account

Given I create an org and leave the default
Then the region is westeurope
```

## Edge cases

- A member's **personal** (non-org) send still uses the default account (EC-041-2) — the helper line under the selector says "Your team's files, not your personal files, are in this region."
- Region value is a closed set (`westeurope`, `eastus2`); a new region = config decision (D-24 track).

## UI notes

- Two cards, no flags-as-icons (text + region name), selected state `--accent-soft`.
- After create, Org settings → Data shows the region as a static chip (no edit control — that's the point).

## Technical notes

- `Organization.Region VARCHAR(16)` (F-ENT-003 DDL); validation against the `StorageAccount` seed rows (TA-3.2).
- Routing happens where the SAS is minted: draft creation (TA-4.2 #1) resolves `session → PrimaryOrgId → region → account`.

## Links

- Feature: `ENT-004-data-residency.md` (FR-041-1, AC-041-1)
- Architecture: TA-3.5 (blob layout by account), TA-11.2 (regional accounts)
- Related: US-040-01 (this is on that screen), US-041-02 (the report proves it)
