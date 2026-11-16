import { describe, expect, it } from 'vitest'

import { toastRole } from './toastRole'

// T-016-02 (FR-016-3): the single-source role mapping — pure function, no DOM needed.
describe('toastRole (T-016-02 — FR-016-3)', () => {
  it('success toasts are polite status regions', () => {
    expect(toastRole('success')).toBe('status')
  })

  it('error toasts are assertive alerts', () => {
    expect(toastRole('error')).toBe('alert')
  })
})
