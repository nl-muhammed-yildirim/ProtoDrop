import { useEffect, useRef } from 'react'
import type { RefObject } from 'react'

/** T-016-03 (EC-016-2, US-016-01 Edge cases): focus trap for modal dialogs — traps Tab within, restores to trigger on unmount. */

export interface UseFocusTrapOptions {
  /** Called when Escape is pressed while the trap is active (e.g. close the dialog). */
  onEscape?: () => void
}

// The single source of truth for what counts as focusable inside a trap: real controls plus explicit tabindex, minus disabled/hidden/tabindex=-1.
const FOCUSABLE_SELECTOR = 'button:not([disabled]), [href], input:not([type="hidden"]), select, textarea, [tabindex]:not([tabindex="-1"])'

export function useFocusTrap(containerRef: RefObject<HTMLElement | null>, options?: UseFocusTrapOptions): void {
  // The element that had focus before the container appeared — restored on cleanup so focus returns to the trigger.
  const previousActiveElement = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (containerRef.current === null) return // container appears/disappears conditionally; nothing to trap yet.
    const container = containerRef.current
    previousActiveElement.current = document.activeElement as HTMLElement | null

    // Focus the FIRST focusable element inside the container on mount — predictable entry point for keyboard users.
    const firstFocusable = container.querySelector<HTMLElement>(FOCUSABLE_SELECTOR)
    if (firstFocusable !== null) firstFocusable.focus()

    // Capture-phase listener: stops Tab/Shift+Tab from escaping past the trap's edges and wraps around.
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        options?.onEscape?.()
        return
      }
      if (event.key !== 'Tab') return

      const focusable = Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
      if (focusable.length === 0) return // nothing to trap — let the browser do its thing.

      const index = focusable.indexOf(document.activeElement as HTMLElement)
      if (event.shiftKey && (index <= 0 || index === -1)) {
        event.preventDefault()
        focusable[focusable.length - 1].focus() // Shift+Tab at/above the first element wraps to the last.
      } else if (!event.shiftKey && (index >= focusable.length - 1 || index === -1)) {
        event.preventDefault()
        focusable[0].focus() // Tab past the last element wraps to the first.
      }
    }

    container.addEventListener('keydown', onKeyDown, true)
    return () => {
      container.removeEventListener('keydown', onKeyDown, true)
      previousActiveElement.current?.focus() // restore focus to the trigger (or wherever it was).
    }
  }, [containerRef.current]) // called once per component mount — the container appears/disappears conditionally.

}
