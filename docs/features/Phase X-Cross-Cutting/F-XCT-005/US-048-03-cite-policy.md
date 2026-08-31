# US-048-03 — Cite the PII list (DPO)

**Feature:** F-XCT-005 — PII Policy (emails, sender names, telemetry) | **Status:** pending

---

**Story:** As the DPO, I want one canonical list — which fields are PII, where raw PII may live, and what erasure covers — that I can quote in a questionnaire or an audit without re-deriving it from the code.
**Actor:** DPO / data-protection officer.
**Goal:** The policy is a document with teeth: the list in this feature file is normative, the code markers are the enforcement, and the erasure cascade is provably in sync.

## Preconditions

- The FR-048-2 closed list exists in the feature file; `ErasureScope.AllColumns` in code mirrors it; the erasure cascade (F-XCT-004) covers it.

## Happy path

1. DPO is asked "where do we store personal data?" → cites FR-048-2 (the closed list of raw-PII locations).
2. "How do you prove it?" → the convention test (US-048-02) that the `[Pii]`-marked columns exactly equal `ErasureScope.AllColumns`, and that the erasure cascade deletes each of them.
3. "What about analytics?" → FR-048-3: file names SHA-256, IPs HMAC, raw emails only under `Pii=true` — with the `f-gdpr-sweep` (US-047-02) closing the loop inside the 90-day retention.
4. "What's Phase 3 going to add?" → FR-048-8: `OrgMember.Email`, IdP group claims, workspace names — already in the closed list, so a Phase 3 migration is checked against the same table.

## Alternative flows

- **A new column is proposed in a migration:** review asks "is it PII? is it in the list? is it in the cascade?" — three yeses, in that order (FR-048-6).
- **A regulator wants the hash scheme:** "SHA-256 for file names (deterministic, for cross-user aggregation), HMAC-SHA256 keyed by a rotating JWT secret for IPs" — one sentence, from FR-048-3.

## Acceptance criteria

```gherkin
Given a data-subject question "what personal data do you keep about me?"
When the DPO answers from the policy
Then the answer cites the FR-048-2 closed list and the telemetry rules (FR-048-3)
And the erasure cascade test proves every listed column is erased

Given a Phase 3 migration adds OrgMember.Email
When the build runs
Then the convention test checks it against the closed list and the erasure cascade
```

## Edge cases

- The policy is a *living* document: any change to the closed list is a change to this feature file + the `ErasureScope` array + a migration note — three places, one PR, reviewed together (that triad is the control).
- `SenderName` on re-homed transfers (EC-048-1) is the one "surprising" entry — the DPO must be able to explain it ("name without address; the transfer survives anonymously"). The policy text says exactly that.
- This story adds no code of its own; it's the *citable* surface. If the DPO can't quote it in one screen, the list is in the wrong place.

## UI notes

- None in-product. The DPO's "UI" is: this feature file + the admin Erasures screen (US-047-03) + the App Insights `Pii=true` filter.

## Technical notes

- `ErasureScope.AllColumns` (in `wa.domain`) is the single source the cascade and the convention test both read — the sync proof is one array, not two documents.
- Quotable sentence for the privacy policy (D-17): "Raw personal data lives in exactly seven places in our database (the closed list in F-XCT-005, FR-048-2); in our analytics it is hashed or HMAC'd; and when you delete your account, every one of those places is erased — telemetry included."

## Links

- Feature: `XCT-005-pii-policy.md` (FR-048-1/2/6/8)
- Plan AC: AC-048-3
- Related: US-047-03 (the erasures screen), US-008-06 (the deletion), US-048-02 (the build gate)
- Architecture: TA-9.4
- Milestone: T-069
