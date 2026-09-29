import { describe, expect, it } from 'vitest';
import { formatBytes } from './formatBytes';

describe('formatBytes (TA-8.5: 1024-based, "GB")', () => {
  const cases: [number, string][] = [
    [0, '0 B'],
    [512, '512 B'],
    [1023, '1023 B'],
    [1024, '1 KB'],
    [1536, '1.5 KB'],
    // Boundary: rounding can push a value up across the unit boundary — this case
    // verifies that 1,048,575 bytes renders as "1 MB" instead of "1024 KB".
    [1_048_575, '1 MB'],
    [1073741824, '1 GB'],
    [2147483648, '2 GB'],
    [1099511627776, '1 TB'],
  ];

  it.each(cases)('formats %i bytes as "%s"', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected);
  });
});
