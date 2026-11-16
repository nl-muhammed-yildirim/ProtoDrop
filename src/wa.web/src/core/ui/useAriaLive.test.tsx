// @vitest-environment jsdom
import { act, createElement } from 'react'
import type { ReactElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it } from 'vitest'

import { ProgressThrottle, summarizeFailures } from './ariaLiveThrottle'
import { useAriaLive } from './useAriaLive'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// Live roots are tracked so afterEach can unmount them (same convention as RecipientPage.test).
const roots: Array<ReturnType<typeof createRoot>> = []

/** Renders a probe component that uses useAriaLive and hands its announce() out via the `out` holder. */
function LiveProbe({ priority, out }: { priority?: 'polite' | 'assertive'; out: { announce?: (message: string) => void } }): ReactElement {
  const live = useAriaLive({ priority })
  out.announce = live.announce // render-time assignment to an external holder — no state update, safe under re-render.
  return createElement('div', null, live.container)
}

function renderProbe(priority?: 'polite' | 'assertive'): { host: HTMLDivElement; announce: (message: string) => void } {
  const out: { announce?: (message: string) => void } = {}
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  roots.push(root)
  act(() => {
    void root.render(createElement(LiveProbe, { priority, out }))
  })
  if (out.announce === undefined) throw new Error('probe did not hand out announce()')
  return { host, announce: out.announce }
}

describe('useAriaLive / ariaLiveThrottle (T-016-01 — FR-016-3)', () => {
  afterEach(() => {
    for (const root of roots.splice(0)) act(() => { root.unmount() })
  })

  it('announces only at 0/25/50/75/100 — intermediate ticks are silent', () => {
    const throttle = new ProgressThrottle()
    const announced: number[] = []
    for (const tick of [0, 10, 24, 25, 30, 49, 50, 60, 74, 75, 80, 99]) {
      const result = throttle.onProgress(tick)
      if (result !== null) announced.push(result)
    }
    expect(announced).toEqual([0, 25, 50, 75]) // EC-016-1: exactly the milestone crossings — nothing else.
  })

  it('a 99 to 100 jump is always announced', () => {
    const throttle = new ProgressThrottle()
    expect(throttle.onProgress(99)).toBeNull() // no prior announcement in this run, and 99 crosses no milestone from null.
    expect(throttle.onProgress(100)).toBe(100) // >=100 is always announced (rule a).
  })

  it('two consecutive failures collapse to one summary announcement', () => {
    const summary = summarizeFailures(['a.bin failed', 'b.bin failed'])
    expect(summary).not.toBeNull() // exactly ONE non-null line for the whole batch…
    expect(typeof summary).toBe('string')
    expect((summary as string).includes('2')).toBe(true) // …and it carries the count.
    expect(summarizeFailures([])).toBeNull() // empty batch → nothing to announce.

    const { host, announce } = renderProbe()
    announce(summary!) // one write — no per-message writes (US-016-02 alt flow).
    const region = host.querySelector('[role="status"]') as HTMLElement
    expect(region.textContent).toBe('2 files failed') // the single summary line verbatim.
  })

  it('the hidden role=status container exists on mount, before any message', () => {
    const { host } = renderProbe() // polite (default) — no announcements made at all.
    const region = host.querySelector('[role="status"][aria-live="polite"]') as HTMLElement | null
    expect(region).not.toBeNull() // the live region is in the DOM immediately after mount, message or not.
  })

  it('assertive renders role=alert', () => {
    const { host } = renderProbe('assertive')
    expect(host.querySelector('[role="status"]')).toBeNull() // no polite element leaks through…
    expect(host.querySelector('[role="alert"]')).not.toBeNull() // …the assertive mapping is role=alert.
  })
})
