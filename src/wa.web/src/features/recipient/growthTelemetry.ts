import { trackClientEvent } from '../../core/telemetry/clientTelemetry'

/**
 * T-003-07 (FR-003-7, TA-10.2): growth-loop telemetry — the panel and the terminal-state CTA both feed
 * the funnel dashboards (F-TRF-012). The events carry a `source` dimension: 'post-download' (the panel
 * shown after a full download) vs 'terminal-state' (the single CTA on expired/unknown/download-limit).
 * T-012-02 formalizes the sink: trackGrowthCta now routes through clientTelemetry.trackClientEvent IN
 * ADDITION to its existing push into growthTelemetryLog (kept so RecipientPage.test.tsx stays green).
 */

export type GrowthEvent = 'growth_cta_shown' | 'growth_cta_clicked'
export type GrowthSource = 'post-download' | 'terminal-state'

// M0 sink: append to an exported record and console.debug — kept for RecipientPage.test.tsx (T-012-02).
export const growthTelemetryLog: Array<{ event: GrowthEvent; source: GrowthSource }> = []

export function trackGrowthCta(event: GrowthEvent, source: GrowthSource): void {
  growthTelemetryLog.push({ event, source })
  console.debug(`[growth-telemetry] ${event} (${source})`)
  // T-012-02 (TA-10.2): route through the formalized client sink — closed list + traceparent + AI transport.
  trackClientEvent(event, { source })
}
