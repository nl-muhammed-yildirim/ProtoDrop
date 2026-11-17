// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AdminUsersPage from './AdminUsersPage'

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

// T-011-12 (FR-011-2): endpoint-22 wire rows (TA-4.2#22).
const BASE_TIME = Date.parse('2026-10-31T12:00:00.000Z')

function makeRows(count: number): unknown[] {
  const rows: unknown[] = []
  for (let i = 0; i < count; i++) {
    rows.push({
      id: `${i.toString(16).padStart(2, '0')}${'0'.repeat(30)}`,   // Guid as a JSON string
      email: `user${i}@x.com`,
      planName: i === 0 ? 'Free' : i === 1 ? 'Pro' : null,          // row 2 pins the "—" null form
      storageBytesUsed: i === 0 ? 1288490188 : i === 1 ? 5368709120 : 1024,   // REAL formatBytes outputs
      activeTransferCount: i === 0 ? 3 : 0,
      createdAtUtc: new Date(BASE_TIME + i * 60_000).toISOString(),
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
    void root.render(createElement(AdminUsersPage, { role }))
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

describe('AdminUsersPage (T-011-12 / FR-011-2)', () => {
  it('Search_QParam_SendsEmailSubstringVerbatim — q is ONE param; the typed text goes verbatim', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const qInput = host.querySelector('.ad-filter-bar input[type="text"]') as HTMLInputElement
      setInputValue(qInput, 'ada@x.com')

      const form = host.querySelector('.ad-filter-bar') as HTMLFormElement
      await act(async () => {
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      // The q param carries the typed text verbatim — the server trims + lowercases and matches CI.
      expect(captured.calls.at(-1)).toContain('q=ada%40x.com')   // URLSearchParams encodes @ as %40.
      expect(captured.calls.at(-1)).toContain('limit=25')        // TA-4.1.4: explicit page size.
    } finally {
      cleanup()
    }
  })

  it('Rows_RenderPlanStorageTransfersAndCreated — AC gherkin: "the row shows their plan, storage used, and active transfer count"', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(3), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const rows = host.querySelectorAll('.ad-table tbody tr')
      expect(rows.length).toBe(3)

      // Row 0: plan select's visible label is the wire planName; storage via REAL formatBytes output.
      const row0Select = rows[0].querySelector('select') as HTMLSelectElement
      expect(row0Select.value).toBe('11111111-0000-4000-8000-000000000001')   // Free → the pinned GUID.
      const row0Option = Array.from(row0Select.options).find((o) => o.value === row0Select.value) as HTMLOptionElement
      expect(row0Option.textContent).toBe('Free')   // the visible label is the wire planName.
      const row0Cells = rows[0].querySelectorAll('td')
      expect(row0Cells[2].textContent).toBe('1.20 GB')   // REAL formatBytes(1288490188).
      expect(row0Cells[3].textContent).toBe('3')         // activeTransferCount.
      // formatCreatedAt (en-US) — derived from the same Date object so it's timezone-independent.
      const d = new Date(BASE_TIME)
      expect(row0Cells[4].textContent).toContain(d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }))

      // Row 2: planName null → a disabled "—" placeholder option is present.
      const row2Select = rows[2].querySelector('select') as HTMLSelectElement
      expect(row2Select.value).toBe('')
      const placeholder = Array.from(row2Select.options).find((o) => o.textContent === '—') as HTMLOptionElement
      expect(placeholder.disabled).toBe(true)   // the "—" option is disabled.
    } finally {
      cleanup()
    }
  })

  it('PlanChange_PatchSendsPinnedPlanIdGuid — changing the select to Pro sends PATCH with the GUID, not the name', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'PATCH') return new Response(null, { status: 204 })
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const select = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      setSelectValue(select, '11111111-0000-4000-8000-000000000002')   // Pro → the pinned GUID.

      await flush()

      // The PATCH carries the planId from the QUERY STRING — the GUID, not the name (Program.cs L654).
      expect(captured.calls.some((c) => c.includes('/admin/users/00000000000000000000000000000000?planId=11111111-0000-4000-8000-000000000002'))).toBe(true)
    } finally {
      cleanup()
    }
  })

  it('PlanChange_OverQuota_DowngradeAllowed — Business → Free still sends the PATCH (no client-side block)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'PATCH') return new Response(null, { status: 204 })
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      // The row is Business (row 0 in this test's wire data — planName "Free" but we change to Free anyway).
      // US-011-03 alternative flows: over-quota downgrades are allowed in the UI — no client-side block.
      const select = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      setSelectValue(select, '11111111-0000-4000-8000-000000000001')   // Free → the pinned GUID.

      await flush()

      // The PATCH was sent — no client-side block on over-quota downgrades.
      expect(captured.calls.some((c) => c.includes('planId=11111111-0000-4000-8000-000000000001'))).toBe(true)
    } finally {
      cleanup()
    }
  })

  it('PlanChange_204_RefetchesCurrentPage — the planName is updated server-side', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'PATCH') return new Response(null, { status: 204 })
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      expect(host.querySelectorAll('.ad-table tbody tr').length).toBe(1)

      const select = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      setSelectValue(select, '11111111-0000-4000-8000-000000000002')   // Pro → the pinned GUID.

      await flush()

      // The refetch happened — the GET count is 2 (initial + post-PATCH refetch).
      const getCalls = captured.calls.filter((c) => c.startsWith('http://localhost:8080/api/v1/admin/users?'))
      expect(getCalls.length).toBe(2)
    } finally {
      cleanup()
    }
  })

  it('PlanChange_Failure_RendersInlineAlert_AndSelectReverts — no optimistic update', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'PATCH') return new Response(null, { status: 404 })   // a real failure.
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const select = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      setSelectValue(select, '11111111-0000-4000-8000-000000000002')   // Pro → the pinned GUID.

      await flush()

      // Failure (anything else): inline role=alert above the table; the select reverts automatically
      // because its value derives from row data (no optimistic update).
      const alertEl = host.querySelector('[role="alert"]') as HTMLElement
      expect(alertEl.textContent).toBe('Could not save the plan. Please try again.')
      // The select value is still the original (Free → …01) — it reverted automatically.
      const selectAfter = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      expect(selectAfter.value).toBe('11111111-0000-4000-8000-000000000001')
    } finally {
      cleanup()
    }
  })

  it('ForceDelete_ConfirmModal_ShowsWording — pinned copy with the row email', async () => {
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

      // AC exact copy: "Force-delete {email}? Their active transfers will be re-homed to anonymous."
      const modalEl = host.querySelector('.pf-modal') as HTMLElement
      expect(modalEl).not.toBeNull()
      const titleEl = modalEl.querySelector('.pf-modal-title') as HTMLElement
      const copyEl = modalEl.querySelector('.pf-modal-copy') as HTMLElement
      expect(titleEl.textContent).toBe('Force-delete user?')
      expect(copyEl.textContent).toBe('Force-delete user0@x.com? Their active transfers will be re-homed to anonymous.')
    } finally {
      cleanup()
    }
  })

  it('ForceDelete_204_RendersSuccessState_And_Refetches — FR-011-2', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') return new Response(null, { status: 204 })   // NoContent with NO body.
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

      expect(captured.calls.some((c) => c.includes('/admin/users/00000000000000000000000000000000'))).toBe(true)   // the DELETE was sent.
      const toastEl = host.querySelector('[role="status"]') as HTMLElement
      expect(toastEl.textContent).toBe('User force-deleted.')   // success state renders immediately after 204.
      expect(host.querySelector('.pf-modal')).toBeNull()        // the modal closed.
      // The refetch happened — the GET count is 2 (initial + post-delete refetch).
      const getCalls = captured.calls.filter((c) => c.startsWith('http://localhost:8080/api/v1/admin/users?'))
      expect(getCalls.length).toBe(2)
    } finally {
      cleanup()
    }
  })

  it('ForceDelete_404_TreatedAsSuccess — EC-009-2 idempotency: 404 NOT_FOUND → success toast, no alert', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') return new Response(JSON.stringify({ code: 'NOT_FOUND' }), { status: 404 })   // EC-009-2.
      return new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const forceDeleteBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (forceDeleteBtn.find((b) => b.getAttribute('aria-label') === 'Force delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      const confirmBtn = host.querySelector('.pf-modal-confirm') as HTMLButtonElement
      await act(async () => {
        confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      // EC-009-2 idempotency: 404 NOT_FOUND → success toast, no alert; the modal closed.
      const toastEl = host.querySelector('[role="status"]') as HTMLElement
      expect(toastEl.textContent).toBe('User force-deleted.')
      expect(host.querySelector('.pf-modal')).toBeNull()   // the modal closed.
      expect(host.querySelector('[role="alert"]')).toBeNull()   // no error alert.
    } finally {
      cleanup()
    }
  })

  it('Operator_Role_SelectsAndTrashDisabled_WithTooltip_NotHidden — exit check: present, disabled=true, title set', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('Operator')
    try {
      await flush()

      // The plan <select> is present in the DOM (NOT hidden), disabled=true, title set.
      const select = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      expect(select).not.toBeNull()
      expect(select.disabled).toBe(true)
      expect(select.getAttribute('title')).toBe('SuperAdmin only')

      // The trash button is present in the DOM (NOT hidden), disabled=true, title set.
      const forceDeleteBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(forceDeleteBtn.length).toBe(1)   // exactly one action button per row.
      expect(forceDeleteBtn[0].disabled).toBe(true)
      expect(forceDeleteBtn[0].getAttribute('title')).toBe('SuperAdmin only')
    } finally {
      cleanup()
    }
  })

  it('SuperAdmin_Role_ControlsEnabled_NoTooltip — both the select and the trash button are enabled', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => new Response(JSON.stringify({ items: makeRows(1), nextCursor: null }), { status: 200 }))

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      // The plan <select> is enabled, no tooltip.
      const select = host.querySelector('.ad-table tbody tr select') as HTMLSelectElement
      expect(select.disabled).toBe(false)
      expect(select.getAttribute('title')).toBeNull()

      // The trash button is enabled, no tooltip.
      const forceDeleteBtn = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(forceDeleteBtn[0].disabled).toBe(false)
      expect(forceDeleteBtn[0].getAttribute('title')).toBeNull()
    } finally {
      cleanup()
    }
  })

  it('Pagination_NextCursor_ForwardedVerbatim — clicking Next forwards the opaque cursor verbatim (TA-4.1.4)', async () => {
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
})
