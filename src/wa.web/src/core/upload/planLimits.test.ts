import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import { formatBytes } from './formatBytes'
import { checkLimits, getPlanLimits, limitMessage, resetPlanLimits, setPlanLimits } from './planLimits'

// Free-plan seed (LimitsRecord.LimitsJson), the only values this module's default carries.
const FREE_MAX_TRANSFER = 5368709120 // 5 GB — MAX_TRANSFER_SIZE free.
const FREE_MAX_FILE = 5368709120      // 5 GB — MAX_SINGLE_FILE free (= transfer size for all plans).

beforeEach(() => {
  resetPlanLimits()
})

afterEach(() => {
  resetPlanLimits()
})

describe('planLimits module', () => {
  it('defaults to the Free guest limits (US-001-03)', () => {
    expect(getPlanLimits()).toEqual({ maxTransferSize: FREE_MAX_TRANSFER, maxSingleFile: FREE_MAX_FILE })
  })

  it('setPlanLimits swaps in a real per-user source without literals elsewhere', () => {
    setPlanLimits({ maxTransferSize: 10737418240, maxSingleFile: 10737418240 }) // Pro seed.
    expect(getPlanLimits().maxTransferSize).toBe(10737418240)
  })
})

describe('checkLimits (pure arithmetic on staged sizes)', () => {
  it('flags a total over the transfer limit', () => {
    const r = checkLimits([{ id: 'a', size: FREE_MAX_TRANSFER }, { id: 'b', size: 1 }], getPlanLimits())
    expect(r.overTotal).toBe(true)
    expect([...r.overFileIds]).toEqual([]) // each file is exactly at the limit.
  })

  it('flags a single oversized row by id (free plan: per-file = transfer, so total is over too)', () => {
    const r = checkLimits([{ id: 'big', size: FREE_MAX_FILE + 1 }], getPlanLimits())
    expect(r.overTotal).toBe(true) // big alone already exceeds the equal transfer limit.
    expect([...r.overFileIds]).toEqual(['big'])
  })

  it('accepts exactly-at-limit sizes (boundary: >, not >=)', () => {
    const r = checkLimits(
      [{ id: 'a', size: FREE_MAX_TRANSFER / 2 }, { id: 'b', size: FREE_MAX_TRANSFER / 2 }],
      getPlanLimits(),
    )
    expect(r.overTotal).toBe(false)
    expect([...r.overFileIds]).toEqual([])
  })

  it('accepts an empty staging list', () => {
    const r = checkLimits([], getPlanLimits())
    expect(r.overTotal).toBe(false)
    expect(r.overFileIds.size).toBe(0)
  })
})

describe('limitMessage (copy names the exact limit via formatBytes)', () => {
  it('total violation: template copy with real sizes — "Transfer size X exceeds the Y limit"', () => {
    // 3 GB + 3 GB = 6.00 GB staged vs a 5.00 GB limit (the AC-001 shape).
    const threeGB = 3 * 1024 ** 3
    const files = [{ id: 'a', size: threeGB }, { id: 'b', size: threeGB }]
    const r = checkLimits(files, getPlanLimits())
    expect(limitMessage(files, r, getPlanLimits())).toBe(
      `Transfer size ${formatBytes(threeGB * 2)} exceeds the ${formatBytes(FREE_MAX_TRANSFER)} limit. Remove files to continue.`,
    )
    // Pin the exact copy too — values must render as formatBytes does, not hand-typed.
    expect(limitMessage(files, r, getPlanLimits())).toBe(
      'Transfer size 6.00 GB exceeds the 5.00 GB limit. Remove files to continue.',
    )
  })

  it('single-file violation marks the actionable one and names the per-file limit', () => {
    const files = [{ id: 'big', size: FREE_MAX_FILE + 1 }]
    const r = checkLimits(files, getPlanLimits())
    expect(limitMessage(files, r, getPlanLimits())).toBe(
      `This file exceeds the ${formatBytes(FREE_MAX_FILE)} per-file limit. Remove it to continue.`,
    )
    expect(limitMessage(files, r, getPlanLimits())).toBe(
      'This file exceeds the 5.00 GB per-file limit. Remove it to continue.',
    )
  })

  it('two oversized files use the plural form', () => {
    const files = [{ id: 'a', size: FREE_MAX_FILE + 1 }, { id: 'b', size: FREE_MAX_FILE + 2 }]
    const r = checkLimits(files, getPlanLimits())
    expect(limitMessage(files, r, getPlanLimits())).toBe(
      `2 files exceed the ${formatBytes(FREE_MAX_FILE)} per-file limit. Remove them to continue.`,
    )
  })

  it('returns null when nothing is over', () => {
    const files = [{ id: 'a', size: FREE_MAX_FILE / 4 }]
    const r = checkLimits(files, getPlanLimits())
    expect(limitMessage(files, r, getPlanLimits())).toBeNull()
  })
})
