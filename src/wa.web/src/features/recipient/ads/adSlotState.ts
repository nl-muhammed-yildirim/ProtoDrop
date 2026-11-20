// US-024-03 (FR-024-2): pure ad-slot state machine. `absent` (no slot) is owned by the placing page's gate.
export type AdSlotState = 'idle' | 'loading' | 'filled' | 'collapsed'
export type AdSlotEvent = 'start' | 'filled' | 'empty' | 'failed' | 'timeout'
export type AdErrorReason = 'timeout' | 'script_error' | 'empty'
export type AdLoadResult = 'filled' | 'empty'

/** Provider port: fill `container`, resolve 'filled' | 'empty', or reject on failure. */
export type AdLoader = (container: HTMLElement) => Promise<AdLoadResult>

export type AdSlotOutcome =
  | { slotId: string; kind: 'impression' }
  | { slotId: string; kind: 'error'; reason: AdErrorReason }

export function nextAdSlotState(state: AdSlotState, event: AdSlotEvent): AdSlotState {
  if (state === 'idle') return event === 'start' ? 'loading' : state
  if (state === 'loading') {
    if (event === 'filled') return 'filled'
    if (event === 'empty' || event === 'failed' || event === 'timeout') return 'collapsed'
  }
  // filled and collapsed are terminal: a late fill after collapse never causes a layout jump.
  return state
}
