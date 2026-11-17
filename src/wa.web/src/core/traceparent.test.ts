import { describe, it, expect, beforeEach } from 'vitest'
import { mintTraceParent, getTraceParent, resetTraceParentForTest } from './traceparent'

describe('traceparent', () => {
  beforeEach(() => {
    // Reset memoized value before each test to ensure isolation
    resetTraceParentForTest()
  })

  describe('mintTraceParent', () => {
    it('returns a valid W3C traceparent format', () => {
      const tp = mintTraceParent()
      expect(tp).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-07$/)
    })

    it('generates unique values across calls', () => {
      const tp1 = mintTraceParent()
      const tp2 = mintTraceParent()
      expect(tp1).not.toEqual(tp2)
    })

    it('has the correct version prefix "00"', () => {
      const tp = mintTraceParent()
      expect(tp.startsWith('00-')).toBe(true)
    })

    it('has a 32-character hex trace id', () => {
      const tp = mintTraceParent()
      const [, traceId] = tp.split('-')
      expect(traceId).toHaveLength(32)
      expect(traceId).toMatch(/^[0-9a-f]{32}$/)
    })

    it('has a 16-character hex parent id', () => {
      const tp = mintTraceParent()
      const [, , parentId] = tp.split('-')
      expect(parentId).toHaveLength(16)
      expect(parentId).toMatch(/^[0-9a-f]{16}$/)
    })

    it('has the sampled flag "07"', () => {
      const tp = mintTraceParent()
      const [, , , flags] = tp.split('-')
      expect(flags).toBe('07')
    })
  })

  describe('getTraceParent', () => {
    it('returns a valid W3C traceparent format', () => {
      const tp = getTraceParent()
      expect(tp).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-07$/)
    })

    it('memoizes the value across calls (same page load)', () => {
      const tp1 = getTraceParent()
      const tp2 = getTraceParent()
      const tp3 = getTraceParent()
      
      expect(tp1).toEqual(tp2)
      expect(tp2).toEqual(tp3)
    })

    it('generates different trace ids after reset (simulating new page load)', () => {
      const tp1 = getTraceParent()
      resetTraceParentForTest()
      const tp2 = getTraceParent()
      
      // Different trace ids after reset
      expect(tp1).not.toEqual(tp2)
      
      // But both should be valid
      expect(tp1).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-07$/)
      expect(tp2).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-07$/)
    })

    it('maintains memoization after reset until next reset', () => {
      const tp1 = getTraceParent()
      resetTraceParentForTest()
      const tp2 = getTraceParent()
      const tp3 = getTraceParent()
      
      // After reset, should be consistent
      expect(tp2).toEqual(tp3)
      // But different from before reset
      expect(tp1).not.toEqual(tp2)
    })
  })
})
