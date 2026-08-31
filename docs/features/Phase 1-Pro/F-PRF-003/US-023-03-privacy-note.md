# US-023-03 — Trust that my recipients' data is handled

**Feature:** F-PRF-003 — Download Analytics | **Status:** pending

---

**Story:** As a Pro user, I want the analytics to be PII-safe and the privacy note to be visible — "Country is estimated from your download's first IP; we keep no raw IP" — so that I can stand behind what we collect.
**Actor:** Pro/Business user, their recipients (data subjects).
**Goal:** only `IpHash` (HMAC) / `UaHash` / `Country CHAR(2)` / browser family stored; the note rendered on the panel.

## Preconditions

- Download events written at SAS mint with `IpHash`, `Country`, `UserAgentFamily` (TA-5.3 `download.completed`).

## Happy path

1. Events carry no raw IP/UA (hashed at write; key in Key Vault).
2. Panel response contains only hashes + country + browser family (asserted in AC-023-2).
3. Privacy note under the country stat: "Country is estimated from your download's first IP; we keep no raw IP."

## Alternative flows

- **Country unavailable**: `??` → "Unknown" (EC-023-2), still PII-safe.
- **UA unparseable**: "Other" bucket (EC-023-3).

## Acceptance criteria

```gherkin
Given a transfer with downloads
When the analytics API is called
Then the response contains no raw IP or UA — only hashes, country, and browser family
And the privacy note text is present on the panel
```

## Edge cases

- HMAC key rotation is a later decision; note states "we keep no raw IP" (true today).
- Telemetry `analytics_viewed` carries only userId + transferId (PII-safe).

## UI notes

- Note: `--fs-tiny` `--fg-muted` under the country stat — one line, no legal walls.

## Technical notes

- `IpHash` = HMAC-SHA256(ip, Key Vault key) at event write (TA-9.4); `UserAgentFamily` column added at write time (migration, ADR note).
- API response schema excludes `IpHash`/`UaHash` entirely (only aggregates).

## Links

- Feature: `PRF-003-download-analytics.md` (FR-023-4, AC-023-2)
- Architecture: TA-9.4, TA-3.2
- Related: F-XCT-005 (PII policy)
