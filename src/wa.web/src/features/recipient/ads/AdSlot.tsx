import './adSlot.css'

import { useAdSlot } from './useAdSlot'
import type { AdLoader, AdSlotOutcome } from './adSlotState'

export type { AdLoader, AdSlotOutcome } from './adSlotState'

interface AdSlotProps {
  slotId: string
  load: AdLoader
  onOutcome?: (o: AdSlotOutcome) => void
}

// US-024-03: renders no text/icon/role; fails invisibly (AC-024-3).
export function AdSlot({ slotId, load, onOutcome }: AdSlotProps) {
  const { state, containerRef } = useAdSlot(load, slotId, onOutcome)
  return (
    <div
      className="rp-ad"
      data-ad-slot={slotId}
      data-state={state}
      aria-hidden={state === 'filled' ? undefined : true}
    >
      <div className="rp-ad-unit" ref={containerRef} />
    </div>
  )
}
