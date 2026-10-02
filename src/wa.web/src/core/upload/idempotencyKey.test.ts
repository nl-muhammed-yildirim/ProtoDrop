import { describe, expect, it } from 'vitest';
import { generateIdempotencyKey } from './idempotencyKey';

// TA-4.1.5: Idempotency keys must be SHA-256 hex strings (64-char lowercase).

describe('generateIdempotencyKey (TA-4.1.5)', () => {
  it('returns a 64-character lowercase hex string', async () => {
    const key = await generateIdempotencyKey('finalize-draft-abc');
    expect(key).toMatch(/^[0-9a-f]{64}$/);
  });

  it('is deterministic — same input produces the same key', async () => {
    const key1 = await generateIdempotencyKey('send-link-xyz-1700000000000');
    const key2 = await generateIdempotencyKey('send-link-xyz-1700000000000');
    expect(key1).toBe(key2);
  });

  it('different inputs produce different keys', async () => {
    const key1 = await generateIdempotencyKey('finalize-draft-abc');
    const key2 = await generateIdempotencyKey('finalize-draft-def');
    expect(key1).not.toBe(key2);
  });

  it('produces the correct SHA-256 for a known input', async () => {
    // SHA-256 of "hello" is well-known.
    const key = await generateIdempotencyKey('hello');
    expect(key).toBe(
      '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824',
    );
  });
});
