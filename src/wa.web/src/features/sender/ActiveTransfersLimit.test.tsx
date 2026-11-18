// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { describe, expect, it } from 'vitest'

import ActiveTransfersLimit from './ActiveTransfersLimit'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

function renderClient(el: React.ReactElement): HTMLDivElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(el)
  })
  return host
}

describe('ActiveTransfersLimit (FR-020-6, AC-020-2)', () => {
  it('shows the exact number and an Upgrade link to /account/plan', () => {
    const host = renderClient(createElement(ActiveTransfersLimit, { details: 'You have 20 active transfers (limit 20). Upgrade to keep sending.' }))
    expect(host.querySelector('.atl-msg')!.textContent).toContain('20 active transfers (limit 20)')
    expect(host.querySelector('.atl-msg')!.getAttribute('role')).toBe('alert')
    expect(host.querySelector('a.atl-upgrade')!.getAttribute('href')).toBe('/account/plan')
    host.remove()
  })

  it('falls back to number-free copy when the details text has no numbers; Upgrade link stays', () => {
    const host = renderClient(createElement(ActiveTransfersLimit, {}))
    expect(host.querySelector('.atl-msg')!.textContent).toBe("You've reached your active transfers limit. Upgrade to keep sending.")
    expect(host.querySelector('a.atl-upgrade')).not.toBeNull()
    host.remove()
  })
})
