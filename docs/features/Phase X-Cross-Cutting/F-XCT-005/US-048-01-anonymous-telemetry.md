# US-048-01 — My file name is anonymous in telemetry

**Feature:** F-XCT-005 — PII Policy (emails, sender names, telemetry) | **Status:** pending

---

**Story:** As a user sending a file with a telling name ("2026-salary-proposal-final_v3.pdf"), I want the telemetry that tells us *that* a 3.1 GB upload completed to never contain *my* file name — so that "we analyze usage" is true without "we read your files."
**Actor:** Any user (the guarantee is about what the platform *keeps*, not what they see).
**Goal:** File names → SHA-256 in every event; IPs → HMAC; raw values only behind the `Pii=true` property (FR-048-3).

## Preconditions

- The TA-9.4 hashing helpers exist: `Sha256Hex`, `HmacIp` (key = `Wa:Jwt:Secret`).
- The user is mid-transfer or just completed one (any event that carries a file name: `upload_started`, `upload_completed`, `zip_generated`, …).

## Happy path

1. User uploads `2026-salary-proposal-final_v3.pdf`.
2. `upload_completed` lands in App Insights with `fileName = sha256("2026-salary-proposal-final_v3.pdf")`, `bytes`, `durationMs`, `retries`.
3. Nowhere in the event properties is the raw name — the CI grep (US-048-02) is what keeps it that way.
4. A download event's IP is `HMAC(ip, key)` — reversible by ops with the key, not by anyone reading the raw column.
5. The *only* raw address in the system for this transfer sits in the PII-flagged property of `email_sent` and in the `EmailRecipient` row (FR-048-2 closed list).

## Alternative flows

- **Two users, same file name:** same hash, both anonymous — accepted (EC-048-4; the hash is for anonymization, not identity).
- **Ops needs the raw name:** they join the hash to the transfer row (raw PII, TA-9.4) — telemetry itself stays clean.
- **Key rotation:** old HMACs aren't reversible with the new key; the policy accepts it (EC-048-3) — events are counts, not a lookup table.

## Acceptance criteria

```gherkin
Given I upload a file with a recognizable name
When upload_completed is emitted
Then the event carries the SHA-256 of the name and no raw name in any property

Given a download occurs
When the download event is emitted
Then the IP is HMAC'd with the JWT secret key
And the raw IP appears only in a Pii=true property if anywhere

Given an event or log must carry an email address
When it is recorded
Then it sits exclusively in a Pii=true flagged property
```

## Edge cases

- File names are hashed, not truncated — "shortened to 20 chars" is *not* a hash and would fail this AC (truncation is a log-display choice, not the storage rule).
- Correlation id is never PII (FR-048-7) — it may travel at any log level; don't over-scrub it (it's the debug handle, US-012-01).
- The raw name *does* exist in `TransferFile.FileName` (the user's own data) — the rule is about **telemetry and logs**, not storage (FR-048-2 is the closed list of where raw PII may live).

## UI notes

- Invisible by design. The user-visible statement of this guarantee is the privacy text (D-17 legal, TBD) — the copy "we never store your file names in analytics" is backed by exactly this mechanism.

## Technical notes

- `Sha256Hex` / `HmacIp` helpers (TA-9.4); events built through the single `IEventPublisher` path so the hashing happens in one place, per event type.
- The PII property flag is the App Insights custom property `Pii=true` (TA-9.4) — DPO queries filter on it.

## Links

- Feature: `XCT-005-pii-policy.md` (FR-048-1/3/4/7)
- Plan AC: AC-048-1, AC-048-2
- Related: US-012-01 (correlation id that is *not* PII), US-012-03 (the dashboards reading these hashed values), US-047-02 (telemetry erasure)
- Architecture: TA-9.4, TA-10.2
- Milestone: T-069
