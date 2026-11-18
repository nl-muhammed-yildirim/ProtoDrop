// @vitest-environment jsdom
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { FeatureGate } from './FeatureGate'
import { resetFeatureGates } from './featureGates'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

async function flush(): Promise<void> {
  for (let i = 0; i < 4; i++) await act(async () => { await Promise.resolve() })
}

function render(...gates: Array<'feature.dataExport' | 'feature.search'>): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    root.render(
      <div>
        {gates.map((g) => (
          <FeatureGate key={g} gate={g}><button data-gate={g}>entry</button></FeatureGate>
        ))}
      </div>,
    )
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

beforeEach(() => resetFeatureGates())
afterEach(() => {
  resetFeatureGates()
  vi.unstubAllGlobals()
})

describe('FeatureGate (US-044-02 / AC-044-2)', () => {
  it('gate off: the entry is absent, not disabled', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ 'feature.dataExport': false }) }))

    const { host, cleanup } = render('feature.dataExport')
    try {
      await flush()
      expect(host.querySelector('button')).toBeNull()
      expect(host.textContent).toBe('')
    } finally {
      cleanup()
    }
  })

  it('gate on: the child renders after the read resolves', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ 'feature.dataExport': true }) }))

    const { host, cleanup } = render('feature.dataExport')
    try {
      expect(host.querySelector('button')).toBeNull() // fail closed until loaded
      await flush()
      expect(host.querySelector('button[data-gate="feature.dataExport"]')).not.toBeNull()
    } finally {
      cleanup()
    }
  })

  it('two mounted gates trigger one fetch', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ 'feature.search': true }) })
    vi.stubGlobal('fetch', fetchMock)

    const { host, cleanup } = render('feature.dataExport', 'feature.search')
    try {
      await flush()
      expect(fetchMock).toHaveBeenCalledTimes(1)
      expect(host.querySelector('button[data-gate="feature.search"]')).not.toBeNull()
      expect(host.querySelector('button[data-gate="feature.dataExport"]')).toBeNull()
    } finally {
      cleanup()
    }
  })
})
