// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import { clientTelemetryLog, setIsDevForTest } from '../../../core/telemetry/clientTelemetry'
import { RECIPIENT_AD_SLOT_ID, trackAdOutcome } from './adTelemetry'

beforeEach(() => {
  clientTelemetryLog.length = 0
  setIsDevForTest(true) // dev fails fast on unknown names: the two ad names must be in the closed list
})
afterEach(() => setIsDevForTest(null))

describe('trackAdOutcome (TA-10.2, FR-024-5)', () => {
  it('impression -> ad_impression with slotId only', () => {
    expect(() => trackAdOutcome({ slotId: RECIPIENT_AD_SLOT_ID, kind: 'impression' })).not.toThrow()
    expect(clientTelemetryLog).toHaveLength(1)
    expect(clientTelemetryLog[0].event).toBe('ad_impression')
    expect(clientTelemetryLog[0].props).toEqual({ slotId: 'recipient-leaderboard' })
  })

  it('error -> ad_error with slotId + reason only (no linkId/plan/url)', () => {
    expect(() => trackAdOutcome({ slotId: RECIPIENT_AD_SLOT_ID, kind: 'error', reason: 'timeout' })).not.toThrow()
    expect(clientTelemetryLog).toHaveLength(1)
    expect(clientTelemetryLog[0].event).toBe('ad_error')
    expect(clientTelemetryLog[0].props).toEqual({ slotId: 'recipient-leaderboard', reason: 'timeout' })
    expect(Object.keys(clientTelemetryLog[0].props).sort()).toEqual(['reason', 'slotId'])
  })
})
