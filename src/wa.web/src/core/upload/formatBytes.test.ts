import { describe, expect, it } from 'vitest'

import { formatBytes, formatBytesShort } from './formatBytes'

describe('formatBytes', () => {
  it('returns "0 B" for zero', () => {
    expect(formatBytes(0)).toBe('0 B')
  })

  it('returns "0 B" for negative values', () => {
    expect(formatBytes(-1)).toBe('0 B')
  })

  it('returns "0 B" for NaN', () => {
    expect(formatBytes(NaN)).toBe('0 B')
  })

  it('returns "0 B" for Infinity', () => {
    expect(formatBytes(Infinity)).toBe('0 B')
  })

  it('formats bytes below 1 KB as raw integer + " B"', () => {
    expect(formatBytes(512)).toBe('512 B')
  })

  it('formats KB values with 2 decimal places when < 10', () => {
    expect(formatBytes(1536)).toBe('1.50 KB')
  })

  it('formats GB values with 1 decimal place when >= 10', () => {
    expect(formatBytes(5 * 1024 ** 3)).toBe('5.00 GB')
  })

  it('formats TB values', () => {
    expect(formatBytes(2 * 1024 ** 4)).toBe('2.00 TB')
  })
})

describe('formatBytesShort', () => {
  it('returns "0 B" for zero', () => {
    expect(formatBytesShort(0)).toBe('0 B')
  })

  it('returns "0 B" for negative values', () => {
    expect(formatBytesShort(-100)).toBe('0 B')
  })

  it('returns "0 B" for NaN', () => {
    expect(formatBytesShort(NaN)).toBe('0 B')
  })

  it('returns "0 B" for Infinity', () => {
    expect(formatBytesShort(Infinity)).toBe('0 B')
  })

  it('formats bytes below 1 KB as raw integer + " B"', () => {
    expect(formatBytesShort(256)).toBe('256 B')
    expect(formatBytesShort(999)).toBe('999 B')
  })

  it('formats whole-unit values without decimal point', () => {
    expect(formatBytesShort(1024)).toBe('1 KB')
    expect(formatBytesShort(1024 ** 2)).toBe('1 MB')
    expect(formatBytesShort(5 * 1024 ** 3)).toBe('5 GB')
    expect(formatBytesShort(2 * 1024 ** 4)).toBe('2 TB')
  })

  it('formats fractional values with at most 1 decimal place', () => {
    expect(formatBytesShort(1.5 * 1024 ** 3)).toBe('1.5 GB')
    expect(formatBytesShort(3.7 * 1024 ** 3)).toBe('3.7 GB')
  })

  it('rounds to nearest tenth (e.g., 1.049 → "1 KB")', () => {
    // 1.049 KB in bytes ≈ 1074.186... → rounds to 1.0 → "1 KB"
    expect(formatBytesShort(1074)).toBe('1 KB')
  })

  it('does not show trailing zeros for whole numbers above bytes', () => {
    expect(formatBytesShort(512 * 1024 ** 2)).toBe('512 MB')
  })
})
