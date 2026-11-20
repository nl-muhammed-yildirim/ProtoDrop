// US-024-01 (FR-024-4, FR-024-6): the one recipient ad slot. Fail closed: no ad config -> nothing renders
// (no slot, no script, no reserve, no telemetry). No CTA, link or "remove ads" copy.
import { useState } from 'react'

import i18n from '../../../core/i18n/init'
import { AdSlot } from './AdSlot'
import type { AdLoader } from './adSlotState'
import { getAdConfig } from './adConfig'
import { createAdSenseLoader } from './adsenseLoader'
import { RECIPIENT_AD_SLOT_ID, trackAdOutcome } from './adTelemetry'

export function RecipientAdPlacement() {
  const [loader] = useState<AdLoader | null>(() => {
    const config = getAdConfig()
    return config ? createAdSenseLoader(config) : null
  })
  if (!loader) return null
  return (
    <AdSlot slotId={RECIPIENT_AD_SLOT_ID} load={loader} label={i18n.t('recipient.active.adLabel')} onOutcome={trackAdOutcome} />
  )
}
