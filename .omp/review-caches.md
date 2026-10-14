# Review: PlansCache + FlagsCache (T-053-03)

## Findings

**1. [minor] TTL boundary at exactly age == ttl is untested — `src/wa.application/Limits/Caches.cs` L18, L40; tests L46/88.**
The check is `clock() - entry.FetchedAtUtc < ttl`, so an entry that is *exactly* 30 s old refetches. That matches the spec's intended reading: AC-053-3 says "younger than 30 s" → no refetch, and "after 30 s the next read refetches"; a strictly-younger guard plus T-053-06's own wording ("a cached entry younger than 30 s is not refetched; after 30 s…") supports `== ttl` being expired. The tests only cover strictly-younger (call count 1) and age = TTL + 1s (call count 2), so the boundary itself is untested. With an injectable clock it's a one-line addition (`clock.Advance(Ttl)` → expect refetch); worth pinning down since "after 30 s" in plain English could be read as `> ttl`. Not spec-breaking — code semantics are defensible and consistent with both the AC gherkin and T-053-06's instructions.

**2. [minor] Cached values are shared mutable instances, not just a concurrency question — `Caches.cs` L19/L41.**
The cache is correct under concurrent access as written: `PlansCache`'s check-and-set races (two simultaneous misses both hit the store; last write wins) only produce redundant reads since every stored value is valid and entries are keyed per plan code. `FlagsCache._entry` is a plain field swap — reference writes/reads don't tear in .NET 10's memory model, so readers see either the old or new Entry atomically (a full `Entry?` read on L40; no torn state). No interceptor needed at this layer. The sharper risk: both caches return *the same instance* to every caller within a TTL window (`Assert.Same` is asserted as intended behavior, AC-053-3 "same instance/row"). If any consumer mutates the returned `Plan` or the rows list (e.g., an override layer writing into a cached Plan), that mutation becomes cross-request shared state. That risk belongs to LimitsProvider/callers — flagging so it's consciously owned, not accidentally assumed safe.

**3. [minor] Failure-not-cached is properly proven — tests L52-65 and L94-107.**
Both failure tests don't just assert propagation: after the throw (call count 1), they clear `Throw`, call again, and assert total calls == 2. If a poison entry had been written on failure, the second read would hit cache and the counter would stay at 1 — so "no poison entry" is genuinely proven for both caches. Code confirms: no `_entries[planCode]`/`_entry = …` write happens before `await`, and L35 starts with a null field. EC-053-3 holds.

**4. [nit] Double clock read on the miss path — `Caches.cs` L18+L22, L40+L44.**
On every cache miss (and on the initial check) `clock()` is called twice: once for the expiry comparison, once to stamp the new Entry. With a wall clock these are ~µs apart; worst case the first read sees an entry as expired and the second stamps it 1 µs later — semantically identical to any other reader observing expiry at that instant. No correctness impact with either fake or real clocks. Leave as-is, or trivially hoist into one local if you prefer single-read purity.

**5. [nit] TTL literal placement is correct; doc comment hardcodes "30 s" — `Caches.cs` L7-10, 29-31.**
No `TimeSpan` literals in the production file (the only `FromSeconds(30)` is in tests, where TA-3.4 explicitly puts it). The XML doc comments say "30 s TTL" twice — documentation drift risk if `Wa:Flags:TtlSeconds` ever changes; they already correctly note the value comes from config right after. Cosmetic.

**6. [nit] StubPlansStore returns one shared Plan for all codes — tests L133-145.**
The same `Plan` instance is returned regardless of plan code, so "Different plans are cached independently" (L28) is proven only by the call counter (2 calls), not by value identity. That's adequate here: per-key isolation in a keyed dictionary plus distinct store hits is exactly what FR-053-4 requires at this layer, and the real seeds test (LimitsProviderTests) covers values. It would mask nothing today; just note that if someone later asserts `Assert.NotSame` between plans' results, the stub would need per-code instances.

**7. [nit] Missing blank line after L26 — tests L26-28.**
No blank line between the close of `Plans_Get_YoungerThanTtl…` and the next `[Fact]`, while every other method in the file (including its FlagsCache counterpart at L70) has one. Inconsistent with the rest of this same file — restore it for uniformity.

## Verdict

**approve-with-nits.** Both caches are small, correct against AC-053-3/EC-053-3 and FR-053-4: TTL guard is right per spec wording, failures propagate uncached with poison-entry genuinely ruled out by the recovery asserts, no TTL literal leaks into production code, and the fakes + fake clock match T-053-06's constraints. The only judgment call — unsynchronized `FlagsCache` field swap and non-atomic check-and-set in `PlansCache` — is acceptable for a read-through cache where all stored values are valid (last-write-wins yields redundant reads, never stale-garbage), so no interceptor or lock is warranted at this layer. Recommend pinning the exact-TTL boundary test while the fake clock is fresh; everything else is cosmetic.
