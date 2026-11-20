import { useEffect, useReducer, useRef } from 'react'
import type { RefObject } from 'react'

import { nextAdSlotState } from './adSlotState'
import type { AdErrorReason, AdLoader, AdSlotEvent, AdSlotOutcome, AdSlotState } from './adSlotState'

// FR-024-2 / UI-Reference §5.3: collapse after 60 s without an ad. A spec UI timing, not a limits.* registry value.
export const AD_LOAD_TIMEOUT_MS = 60_000

function scheduleFrame(cb: () => void): () => void {
  if (typeof requestAnimationFrame === 'function') {
    const id = requestAnimationFrame(cb)
    return () => cancelAnimationFrame(id)
  }
  const id = setTimeout(cb, 0)
  return () => clearTimeout(id)
}

/** Drives the ad slot: deferred start (TA-8.4), single settle, silent failure, unmount safety. */
export function useAdSlot(
  load: AdLoader,
  slotId: string,
  onOutcome?: (o: AdSlotOutcome) => void,
): { state: AdSlotState; containerRef: RefObject<HTMLDivElement> } {
  const [state, dispatch] = useReducer(nextAdSlotState, 'idle' as AdSlotState)
  const containerRef = useRef<HTMLDivElement>(null)
  const loadRef = useRef(load)
  const outcomeRef = useRef(onOutcome)
  const slotRef = useRef(slotId)

  useEffect(() => {
    loadRef.current = load
    outcomeRef.current = onOutcome
    slotRef.current = slotId
  })

  useEffect(() => {
    let cancelled = false
    let settled = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const abort = new AbortController()

    const settle = (event: AdSlotEvent, reason?: AdErrorReason) => {
      if (cancelled || settled) return
      settled = true
      if (timer !== undefined) clearTimeout(timer)
      if (event === 'timeout') abort.abort() // release the loader's observer
      dispatch(event)
      if (event === 'filled') {
        outcomeRef.current?.({ slotId: slotRef.current, kind: 'impression' })
      } else {
        outcomeRef.current?.({ slotId: slotRef.current, kind: 'error', reason: reason ?? 'script_error' })
      }
    }

    const cancelFrame = scheduleFrame(() => {
      if (cancelled) return
      dispatch('start')
      timer = setTimeout(() => settle('timeout', 'timeout'), AD_LOAD_TIMEOUT_MS)
      const el = containerRef.current
      if (!el) {
        settle('failed', 'script_error')
        return
      }
      Promise.resolve()
        .then(() => loadRef.current(el, abort.signal))
        .then(
          (result) => (result === 'filled' ? settle('filled') : settle('empty', 'empty')),
          () => settle('failed', 'script_error'), // silent: no logging (AC-024-3)
        )
    })

    return () => {
      cancelled = true
      cancelFrame()
      abort.abort()
      if (timer !== undefined) clearTimeout(timer)
    }
  }, [])

  return { state, containerRef }
}
