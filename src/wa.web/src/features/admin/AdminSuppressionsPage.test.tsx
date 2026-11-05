// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AdminSuppressionsPage from './AdminSuppressionsPage'

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

// Mirror AdminTransfersPage.test.tsx L13–L41: hand-stub fetch with a capture closure (no extra packages).
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

// T-011-13 (FR-011-2 / FR-011-5): endpoint-24 wire rows — one row pinning senderEmail null → '—'.
const SUPPRESSION_ID = '00000000000000000000000000000001'   // Guid 'n'-form (32 hex chars, no dashes).
const ROWS = [
  {
    id: SUPPRESSION_ID,
    address: 'user@x.com',
    senderEmail: null,   // row 0 pins the "—" null form.
    createdAtUtc: '2026-10-31T12:00:00.000Z',
  },
]

function suppressionsResponder(req: Request): Response {
  if (req.method === 'DELETE') return new Response(null, { status: 200 })   // endpoint 24 DELETE → 200 OK.
  return new Response(JSON.stringify(ROWS), { status: 200 })
}

// Render the page component directly (AdminShell's role narrowing is covered by AdminShell.test.tsx).
function renderPage(role: 'SuperAdmin' | 'Operator'): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(AdminSuppressionsPage, { role }))
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

// Flush microtasks (the fetch promise chain) without running fake timers.
async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve() })
  await act(async () => { await Promise.resolve() })
}

beforeEach(() => {
  installLocalStorageShim()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AdminSuppressionsPage (T-011-13 / FR-011-2, FR-011-5)', () => {
  it('Rows_RenderAddressSenderDate — address, senderEmail ?? "—", formatted date', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, suppressionsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const rowCells = host.querySelectorAll('.ad-table tbody tr')[0].querySelectorAll('td')
      expect(rowCells[0].textContent).toBe(ROWS[0].address)   // Address.
      expect(rowCells[1].textContent).toBe('—')               // senderEmail null → '—'.
      const d = new Date(ROWS[0].createdAtUtc)
      expect(rowCells[2].textContent).toContain(
        d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
      )   // Date — formatCreatedAt (en-US).
    } finally {
      cleanup()
    }
  })

  it('Remove_200_RemovesRow_And_Refetches — FR-011-5: DELETE sent; 200 OK → row gone + refetch', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') return new Response(null, { status: 200 })   // KEEP 200 — NOT NoContent.
      return new Response(JSON.stringify(ROWS), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      expect(host.querySelectorAll('.ad-table tbody tr').length).toBe(1)   // the row renders.

      const removeBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (removeBtn.find((b) => b.getAttribute('aria-label') === 'Remove') as HTMLButtonElement).dispatchEvent(
          new MouseEvent('click', { bubbles: true }),
        )
        await Promise.resolve()   // flush the DELETE's promise chain (the stub is async).
        await Promise.resolve()   // flush the refetch's promise chain.
      })

      expect(captured.calls.some((c) => c.includes(`/admin/suppressions/${SUPPRESSION_ID}`))).toBe(true)   // the DELETE was sent.
      const toastEl = host.querySelector('[role="status"]') as HTMLElement
      expect(toastEl.textContent).toBe('Suppression removed.')   // success state renders immediately after 200 OK.
      // The refetch happened — the GET count is 2 (initial + post-delete refetch).
      const getCalls = captured.calls.filter((c) => c.endsWith('/admin/suppressions'))
      expect(getCalls.length).toBe(2)
    } finally {
      cleanup()
    }
  })

  it('Operator_Role_RemoveDisabled_WithTooltip_NotHidden — AC-011-3: Remove present, disabled=true, title set', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, suppressionsResponder)

    const { host, cleanup } = renderPage('Operator')
    try {
      await flush()

      const removeBtns = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(removeBtns.length).toBe(1)   // the Remove button is present in the DOM (NOT hidden).
      for (const btn of removeBtns) {
        expect(btn.disabled).toBe(true)   // Operator → disabled.
        expect(btn.getAttribute('title')).toBe('SuperAdmin only')   // tooltip is set.
      }
    } finally {
      cleanup()
    }
  })

  it('SuperAdmin_Role_RemoveEnabled — SuperAdmin → enabled, no tooltip', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, suppressionsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const removeBtns = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(removeBtns.length).toBe(1)
      for (const btn of removeBtns) {
        expect(btn.disabled).toBe(false)   // SuperAdmin → enabled.
        expect(btn.getAttribute('title')).toBeNull()   // no tooltip needed.
      }
    } finally {
      cleanup()
    }
  })
})
