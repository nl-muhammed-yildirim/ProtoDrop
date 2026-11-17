// @vitest-environment jsdom
import { useRef } from 'react'
import type { ReactElement } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'

import { useFocusTrap } from './useFocusTrap'

/** A modal dialog whose container is trapped by useFocusTrap — the hook under test. */
function DialogHost({ onEscape }: { onEscape?: () => void }): ReactElement {
  const ref = useRef<HTMLDivElement>(null)
  useFocusTrap(ref, { onEscape })
  return (
    <div role="dialog" aria-label="Test dialog" ref={ref}>
      <button type="button">First</button>
      <button type="button">Middle</button>
      <button type="button">Last</button>
    </div>
  )
}

/** Wrapper with the trigger button OUTSIDE the dialog — focus must return to it when the dialog unmounts. */
function Page({ open }: { open: boolean }): ReactElement {
  const triggerRef = useRef<HTMLButtonElement>(null)
  return (
    <>
      <button type="button" ref={triggerRef}>Trigger</button>
      {open && <DialogHost />}
    </>
  )
}

describe('useFocusTrap (T-016-03 — EC-016-2, US-016-01 Edge cases)', () => {
  afterEach(cleanup)

  it('focuses first focusable element on mount', () => {
    render(<DialogHost />)
    expect(document.activeElement).toBe(screen.getByText('First')) // EC-016-2: predictable entry point.
  })

  it('Tab wraps from last to first', () => {
    render(<DialogHost />)
    const container = screen.getByRole('dialog') as HTMLElement
    const buttons = Array.from(container.querySelectorAll<HTMLButtonElement>('button'))
    buttons[buttons.length - 1].focus() // start at the LAST element…
    fireEvent.keyDown(container, { key: 'Tab' }) // …and Tab must wrap to the first.
    expect(document.activeElement).toBe(buttons[0])
  })

  it('Shift+Tab wraps from first to last', () => {
    render(<DialogHost />)
    const container = screen.getByRole('dialog') as HTMLElement
    const buttons = Array.from(container.querySelectorAll<HTMLButtonElement>('button'))
    buttons[0].focus() // start at the FIRST element…
    fireEvent.keyDown(container, { key: 'Tab', shiftKey: true }) // …and Shift+Tab must wrap to the last.
    expect(document.activeElement).toBe(buttons[buttons.length - 1])
  })

  it('restores focus to trigger on unmount', () => {
    const view = render(<Page open={false} />)
    const trigger = screen.getByText('Trigger') as HTMLButtonElement
    trigger.focus() // the element that had focus before the dialog appeared.
    view.rerender(<Page open={true} />); expect(document.activeElement).toBe(screen.getByText('First')) // trap took over…
    view.rerender(<Page open={false} />) // …and unmounting restores it to the trigger (US-016-01 edge case).
    expect(document.activeElement).toBe(trigger)
  })

  it('Escape calls onEscape callback', () => {
    const onEscape = vi.fn()
    render(<DialogHost onEscape={onEscape} />)
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onEscape).toHaveBeenCalledTimes(1) // exactly one call per Escape.
  })
})
