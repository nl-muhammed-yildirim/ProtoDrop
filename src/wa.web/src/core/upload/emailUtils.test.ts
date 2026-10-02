import { describe, expect, it } from 'vitest';
import { MAX_EMAILS, parseRecipients } from './emailUtils';

describe('parseRecipients (EC-006-1)', () => {
  it('splits on mixed delimiters (comma + newline + semicolon)', () => {
    const result = parseRecipients('a@x.com,b@y.com\nc@z.com; d@w.com');
    expect(result.valid).toEqual(['a@x.com', 'b@y.com', 'c@z.com', 'd@w.com']);
    expect(result.invalid).toEqual([]);
  });

  it('drops trailing spaces and empty tokens', () => {
    const result = parseRecipients('a@x.com , ,  b@y.com  ');
    expect(result.valid).toEqual(['a@x.com', 'b@y.com']);
    expect(result.invalid).toEqual([]);
  });

  it('deduplicates (first occurrence wins)', () => {
    const result = parseRecipients('a@x.com a@x.com A@X.COM');
    expect(result.valid).toEqual(['a@x.com']);
    expect(result.invalid).toEqual([]);
  });

  it('normalizes case: "A@X.COM" → "a@x.com"', () => {
    const result = parseRecipients('A@X.COM');
    expect(result.valid).toEqual(['a@x.com']);
  });

  it('keeps Gmail aliases distinct (not deduped)', () => {
    const result = parseRecipients('user+1@gmail.com user+2@gmail.com');
    expect(result.valid).toEqual(['user+1@gmail.com', 'user+2@gmail.com']);
  });

  it.each([
    ['bad-address'],
    ['no-at-sign'],
    ['missing.tld'], // single-char TLD
  ])('rejects invalid address "%s"', (address) => {
    const result = parseRecipients(address);
    expect(result.valid).toEqual([]);
    expect(result.invalid).toEqual([address]);
  });

  it('returns empty arrays for empty string input', () => {
    const result = parseRecipients('');
    expect(result).toEqual({ valid: [], invalid: [] });
  });
});

describe('MAX_EMAILS (EC-006-1)', () => {
  it('is 20', () => {
    expect(MAX_EMAILS).toBe(20);
  });
});
