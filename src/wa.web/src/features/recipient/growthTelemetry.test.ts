import { beforeEach, describe, expect, it } from 'vitest'

import { clientTelemetryLog } from '../../core/telemetry/clientTelemetry'
import { resetTraceParentForTest } from '../../core/traceparent'
import { growthTelemetryLog, trackGrowthCta } from './growthTelemetry'

// ── T-012-02 (TA-10.2): growth CTA routes through the formalized client sink. ──
describe('growthTelemetry (T-012-02)', () => {
  beforeEach(() => {
    resetTraceParentForTest() // fresh page load per case — the correlation id is minted once per page load.
    growthTelemetryLog.splice(0) // module-level record — keep tests hermetic.
    clientTelemetryLog.splice(0) // module-level record — keep tests hermetic.
  })

  it('trackGrowthCta routes through the sink: both logs contain the event with source and traceparent', () => {
    trackGrowthCta('growth_cta_shown', 'post-download')

    expect(growthTelemetryLog).toHaveLength(1)
    expect(growthTelemetryLog[0]).toEqual({ event: 'growth_cta_shown', source: 'post-download' })

    expect(clientTelemetryLog).toHaveLength(1)
    expect(clientTelemetryLog[0].event).toBe('growth_cta_shown')
    expect(clientTelemetryLog[0].props).toEqual({ source: 'post-download' })
    expect(clientTelemetryLog[0].traceparent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-07$/)
  })

  it('trackGrowthCta with terminal-state source routes through the sink', () => {
    trackGrowthCta('growth_cta_clicked', 'terminal-state')

    expect(growthTelemetryLog).toHaveLength(1)
    expect(growthTelemetryLog[0]).toEqual({ event: 'growth_cta_clicked', source: 'terminal-state' })

    expect(clientTelemetryLog).toHaveLength(1)
    expect(clientTelemetryLog[0].event).toBe('growth_cta_clicked')
    expect(clientTelemetryLog[0].props).toEqual({ source: 'terminal-state' })
  })
})
