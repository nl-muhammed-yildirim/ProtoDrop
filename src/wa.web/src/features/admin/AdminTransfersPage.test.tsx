// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AdminTransfersPage from './AdminTransfersPage'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// jsdom's built-in localStorage is present, but the App.test.tsx / FilesPage.test.tsx pattern installs
// an in-memory shim so session helpers round-trip deterministically — mirror it for hermeticity.
function installLocalStorageShim(): void {
  if (typeof window === 'undefined') return
  const store = new Map<string, string>()
  const ls: Storage = {
    get length() { return store.size },
    clear: () => store.clear(),
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => { store.delete(k); },
    setItem: (k: string, v: string) => { store.set(k, String(v)); },
  }
  Object.defineProperty(window, 'localStorage', { value: ls, configurable: true })
}

// Mirror FilesPage.test.tsx: hand-stub fetch with a capture closure (no extra packages).
function stubFetch(
  captured: { calls: string[] },
  responder?: (req: Request) => Response | Promise<Response>,
): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const url = typeof input === 'string' ? input : String(input)
      captured.calls.push(url)
      const req = new Request(url, { method: init?.method ?? 'GET' })
      return responder ? await responder(req) : new Response(null, { status: 200 })
    },
  )
}

// T-011-11 (FR-011-3): endpoint-21 wire rows (TA-4.2#21) — id is the T-011-11 projection addition.
const BASE_TIME = Date.parse('2026-10-31T12:00:00.000Z')

function makeRows(count: number): unknown[] {
  const rows: unknown[] = []
  for (let i = 0; i < count; i++) {
    rows.push({
      id: `${i.toString(16).padStart(2, '0')}${'0'.repeat(30)}`,   // Guid 'n'-form (32 hex chars)
      linkId: `link${String(i + 1).padStart(2, '0')}`,             // 8-char Crockford
      ownerEmail: i === 0 ? null : `user${i}@x.com`,                // row 0 pins the "—" null form
      status: 1,
      fileCount: i === 0 ? 3 : 1,                                  // row 0 pins the modal copy (AC)
      totalBytes: i === 0 ? 1288490188 : 1024,
      createdAtUtc: new Date(BASE_TIME + i * 60_000).toISOString(),
      downloadsCount: i === 0 ? 87 : 0,
    })
  }
  return rows
}

// Render the page component directly (AdminShell's role narrowing is covered by AdminShell.test.tsx).
function renderPage(role: 'SuperAdmin' | 'Operator'): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(AdminTransfersPage, { role }))
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

// Flush microtasks (the fetch promise chain) without running fake timers.
async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve() })
  await act(async () => { await Promise.resolve() })
}

function setInputValue(el: HTMLInputElement, value: string): void {
  const nativeSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
  act(() => {
    nativeSetter.call(el, value)
    el.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

function setSelectValue(el: HTMLSelectElement, value: string): void {
  const nativeSetter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!
  act(() => {
    nativeSetter.call(el, value)
    el.dispatchEvent(new Event('change', { bubbles: true }))
  })
}

beforeEach(() => {
  installLocalStorageShim()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AdminTransfersPage (T-011-11 / FR-011-3, FR-011-4)', () => {
  it('Search_SendsQParam_WithTypedText — q is ONE param; the typed text goes verbatim (server OR semantics)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const qInput = host.querySelector('.ad-filter-bar input[type="text"]') as HTMLInputElement
      setInputValue(qInput, 'ab3def5g')

      const form = host.querySelector('.ad-filter-bar') as HTMLFormElement
      await act(async () => {
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      // The q param carries the typed text verbatim — the server does LinkId exact CI OR EmailRecipient LIKE %q%.
      expect(captured.calls.at(-1)).toContain('q=ab3def5g')
      expect(captured.calls.at(-1)).toContain('limit=25')   // TA-4.1.4: explicit page size.
    } finally {
      cleanup()
    }
  })

  it('Search_StatusSelect_SendsStatusWord', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const select = host.querySelector('.ad-filter-bar select') as HTMLSelectElement
      setSelectValue(select, 'download_limit')

      const form = host.querySelector('.ad-filter-bar') as HTMLFormElement
      await act(async () => {
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      expect(captured.calls.at(-1)).toContain('status=download_limit')   // the wire word, verbatim.
    } finally {
      cleanup()
    }
  })

  it('Search_SizeRange_SendsMinAndMaxBytes_Paired', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const numberInputs = Array.from(host.querySelectorAll('.ad-filter-bar input[type="number"]')) as HTMLInputElement[]
      setInputValue(numberInputs[0], '100')
      setInputValue(numberInputs[1], '200')

      const form = host.querySelector('.ad-filter-bar') as HTMLFormElement
      await act(async () => {
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      // Both halves of the pair are sent (the server rejects an unpaired range with 400 VALIDATION).
      expect(captured.calls.at(-1)).toContain('minBytes=100')
      expect(captured.calls.at(-1)).toContain('maxBytes=200')
    } finally {
      cleanup()
    }
  })

  it('Search_DateRange_SendsFromAndTo_Paired', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const dateInputs = Array.from(host.querySelectorAll('.ad-filter-bar input[type="date"]')) as HTMLInputElement[]
      setInputValue(dateInputs[0], '2026-01-01')
      setInputValue(dateInputs[1], '2026-01-31')

      const form = host.querySelector('.ad-filter-bar') as HTMLFormElement
      await act(async () => {
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      expect(captured.calls.at(-1)).toContain('from=2026-01-01')
      expect(captured.calls.at(-1)).toContain('to=2026-01-31')
    } finally {
      cleanup()
    }
  })

  it('Pagination_NextCursor_AdvancesPage — clicking Next forwards the opaque cursor verbatim (TA-4.1.4)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      const url = new URL(req.url, 'http://localhost:8080')
      if (url.searchParams.get('cursor') === 'CURSOR_PAGE2') {
        return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
      }
      return new Response(JSON.stringify({ items: makeRows(25), nextCursor: 'CURSOR_PAGE2' }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      expect(host.querySelectorAll('.ad-table tbody tr').length).toBe(25)   // page 1 renders 25 rows.

      const nextBtn = host.querySelector('.mf-next') as HTMLButtonElement
      expect(nextBtn.disabled).toBe(false)   // a next page exists.
      await act(async () => {
        nextBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      const rowEls = host.querySelectorAll('.ad-table tbody tr')
      expect(rowEls.length).toBe(1)          // exactly the 2nd page renders (page REPLACES rows).
      expect(captured.calls.at(-1)).toContain('cursor=CURSOR_PAGE2')   // the opaque cursor is forwarded verbatim.

      const nextBtnAfter = host.querySelector('.mf-next') as HTMLButtonElement
      expect(nextBtnAfter.disabled).toBe(true)   // nextCursor null → last page (Next disabled).
    } finally {
      cleanup()
    }
  })

  it('Pagination_NullNextCursor_DisablesNext — no next page → Next is disabled', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(25), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      const nextBtn = host.querySelector('.mf-next') as HTMLButtonElement
      expect(nextBtn.disabled).toBe(true)   // nextCursor null → last page.
      const prevBtn = host.querySelector('.mf-prev') as HTMLButtonElement
      expect(prevBtn.disabled).toBe(true)   // page 0 → Previous disabled.
    } finally {
      cleanup()
    }
  })

  it('ForceDelete_ConfirmModal_ShowsWording_WithFileCountAndSize — AC copy from ROW data + formatBytes', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const forceDeleteBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(forceDeleteBtn.find((b) => b.getAttribute('aria-label') === 'Force delete')).toBeDefined()
      await act(async () => {
        (forceDeleteBtn.find((b) => b.getAttribute('aria-label') === 'Force delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      // AC exact copy: "Force-delete transfer {linkId}? {fileCount} files, {formatBytes(totalBytes)}."
      const modalEl = host.querySelector('.pf-modal') as HTMLElement
      expect(modalEl).not.toBeNull()
      const titleEl = modalEl.querySelector('.pf-modal-title') as HTMLElement
      const copyEl = modalEl.querySelector('.pf-modal-copy') as HTMLElement
      expect(titleEl.textContent).toBe('Force-delete transfer?')
      expect(copyEl.textContent).toBe('Force-delete transfer link01? 3 files, 1.20 GB.')   // REAL formatBytes output.
    } finally {
      cleanup()
    }
  })

  it('ForceDelete_204_RendersSuccessState_And_Refetches — FR-011-4 / EC-011-2', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') {
        return new Response(null, { status: 204 })   // endpoint 21 DELETE returns NoContent with NO body on success.
      }
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      expect(host.querySelectorAll('.ad-table tbody tr').length).toBe(1)

      const forceDeleteBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (forceDeleteBtn.find((b) => b.getAttribute('aria-label') === 'Force delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      const confirmBtn = host.querySelector('.pf-modal-confirm') as HTMLButtonElement
      await act(async () => {
        confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()   // flush the DELETE's promise chain (the stub is async).
        await Promise.resolve()   // flush the refetch's promise chain.
      })

      expect(captured.calls.some((c) => c.includes('/admin/transfers/00000000000000000000000000000000'))).toBe(true)   // the DELETE was sent.
      const toastEl = host.querySelector('[role="status"]') as HTMLElement
      expect(toastEl.textContent).toBe('Transfer force-deleted.')   // success state renders immediately after 204.
      expect(host.querySelector('.pf-modal')).toBeNull()   // the modal closed.
      // The refetch happened — the GET count is 2 (initial + post-delete refetch).
      const getCalls = captured.calls.filter((c) => c.startsWith('http://localhost:8080/api/v1/admin/transfers?'))
      expect(getCalls.length).toBe(2)
    } finally {
      cleanup()
    }
  })

  it('ForceDelete_404_RendersInlineAlert — a real failure keeps the modal open with role=alert', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') {
        return new Response(null, { status: 500 })   // a genuine failure (NOT the EC-011-2 success shape).
      }
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      expect(host.querySelectorAll('.ad-table tbody tr').length).toBe(1)

      const forceDeleteBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (forceDeleteBtn.find((b) => b.getAttribute('aria-label') === 'Force delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      const confirmBtn = host.querySelector('.pf-modal-confirm') as HTMLButtonElement
      await act(async () => {
        confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
      })

      // Failure (anything else): inline role=alert error in the modal, modal stays open.
      const alertEl = host.querySelector('[role="alert"]') as HTMLElement
      expect(alertEl.textContent).toBe('Could not force-delete the transfer. Please try again.')
      expect(host.querySelector('.pf-modal')).not.toBeNull()   // the modal is still open.
      expect(host.querySelectorAll('.ad-table tbody tr').length).toBe(1)   // the row is NOT removed on failure.
    } finally {
      cleanup()
    }
  })

  it('Operator_Role_ActionsDisabled_WithTooltip_NotHidden — exit check: buttons present, disabled=true, title set', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('Operator')
    try {
      await flush()

      const actionBtns = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(actionBtns.length).toBe(2)   // both buttons are present in the DOM (NOT hidden).
      for (const btn of actionBtns) {
        expect(btn.disabled).toBe(true)   // Operator → disabled.
        expect(btn.getAttribute('title')).toBe('SuperAdmin only')   // tooltip is set.
      }
    } finally {
      cleanup()
    }
  })

  it('SuperAdmin_Role_ActionsEnabled — both buttons enabled, no tooltip', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const actionBtns = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(actionBtns.length).toBe(2)
      for (const btn of actionBtns) {
        expect(btn.disabled).toBe(false)   // SuperAdmin → enabled.
        expect(btn.getAttribute('title')).toBeNull()   // no tooltip needed.
      }
    } finally {
      cleanup()
    }
  })
})
