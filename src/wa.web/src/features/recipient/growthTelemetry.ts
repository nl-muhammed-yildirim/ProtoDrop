/**
 * T-003-07 (FR-003-7, TA-10.2): growth-loop telemetry — the panel and the terminal-state CTA both feed
 * the funnel dashboards (F-TRF-012). The events carry a `source` dimension: 'post-download' (the panel
 * shown after a full download) vs 'terminal-state' (the single CTA on expired/unknown/download-limit).
 * M0 client-sink deviation (ADR in Open-Decisions-and-Constants.md Part 3): no AI pipeline at M0 — this
 * module follows the UploadEngine convention (exported array + console.debug), test-overridable.
 */

export type GrowthEvent = 'growth_cta_shown' | 'growth_cta_clicked'
export type GrowthSource = 'post-download' | 'terminal-state'

// M0 sink: append to an exported record and console.debug — F-TRF-012 formalizes the sink + updates TA-10.2.
export const growthTelemetryLog: Array<{ event: GrowthEvent; source: GrowthSource }> = []

export function trackGrowthCta(event: GrowthEvent, source: GrowthSource): void {
  growthTelemetryLog.push({ event, source })
  console.debug(`[growth-telemetry] ${event} (${source})`)
}
