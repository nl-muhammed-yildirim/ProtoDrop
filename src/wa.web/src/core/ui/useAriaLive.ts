import { createElement, useRef } from 'react'
import type { CSSProperties } from 'react'

/**
 * T-016-01 (FR-016-3): hidden ARIA live region for upload progress/failure announcements.
 * The container div is rendered UNCONDITIONALLY so the live region exists on mount before any message;
 * announce() writes textContent directly through a ref — no React state, announcing never re-renders the screen.
 */

// Visually-hidden clip pattern (inline style — no CSS file): keeps the element in the accessibility tree while invisible.
const HIDDEN_STYLE: CSSProperties = {
  position: 'absolute', width: '1px', height: '1px', padding: '0', margin: '-1px', overflow: 'hidden', clip: 'rect(0, 0, 0, 0)', whiteSpace: 'nowrap', border: '0',
}

export function useAriaLive(opts: { priority?: 'polite' | 'assertive' }): { container: JSX.Element; announce: (message: string) => void } {
  const ref = useRef<HTMLDivElement | null>(null)
  // The single source for the priority→ARIA mapping (FR-016-3): polite → role=status/aria-live=polite, assertive → role=alert.
  const container = opts.priority === 'assertive'
    ? createElement('div', { ref, style: HIDDEN_STYLE, role: 'alert' })
    : createElement('div', { ref, style: HIDDEN_STYLE, role: 'status', 'aria-live': 'polite' })

  // Direct DOM write via the ref — SSR-safe (ref is null until mount; no browser-only access at render time).
  const announce = (message: string): void => {
    if (ref.current !== null) ref.current.textContent = message
  }

  return { container, announce }
}
