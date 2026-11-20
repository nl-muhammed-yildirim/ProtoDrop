import { describe, expect, it } from 'vitest'

import { nextAdSlotState, type AdSlotEvent, type AdSlotState } from './adSlotState'

const events: AdSlotEvent[] = ['start', 'filled', 'empty', 'failed', 'timeout']

describe('nextAdSlotState (FR-024-2)', () => {
  it('idle + start -> loading', () => {
    expect(nextAdSlotState('idle', 'start')).toBe('loading')
  })

  it('loading + filled -> filled', () => {
    expect(nextAdSlotState('loading', 'filled')).toBe('filled')
  })

  it.each(['empty', 'failed', 'timeout'] as const)('loading + %s -> collapsed', (e) => {
    expect(nextAdSlotState('loading', e)).toBe('collapsed')
  })

  it('loading + start stays loading', () => {
    expect(nextAdSlotState('loading', 'start')).toBe('loading')
  })

  it.each(events.filter((e) => e !== 'start'))('idle ignores %s', (e) => {
    expect(nextAdSlotState('idle', e)).toBe('idle')
  })

  it.each(['filled', 'collapsed'] as AdSlotState[])('%s absorbs every event', (s) => {
    for (const e of events) expect(nextAdSlotState(s, e)).toBe(s)
  })
})
