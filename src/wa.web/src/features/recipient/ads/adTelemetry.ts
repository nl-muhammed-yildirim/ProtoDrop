// US-024-01 (D2, TA-10.2): ad slot outcomes -> client telemetry. Props are exactly slotId (+ reason on error):
// never the linkId, plan, URL or any user value (FR-024-5). Object literals so the PII guard (R5) sees the keys.
import { trackClientEvent } from '../../../core/telemetry/clientTelemetry'
import type { AdSlotOutcome } from './adSlotState'

export const RECIPIENT_AD_SLOT_ID = 'recipient-leaderboard'

export function trackAdOutcome(outcome: AdSlotOutcome): void {
  if (outcome.kind === 'impression') {
    trackClientEvent('ad_impression', { slotId: outcome.slotId })
  } else {
    trackClientEvent('ad_error', { slotId: outcome.slotId, reason: outcome.reason })
  }
}
