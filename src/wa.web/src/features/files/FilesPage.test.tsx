// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import App from '../../App'
import type { StoredProfile } from '../../core/auth/session'
import { clearSession, writeSession } from '../../core/auth/session'
import { formatBytes } from '../../core/upload/formatBytes'
import { FILES_PATH } from './filesRoute'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// jsdom's built-in localStorage is present, but App.test.tsx installs an in-memory shim for the
// signed-in App tests; mirror it so writeSession/readSession round-trip deterministically.
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

// Mirror ProfilePage.test.tsx: hand-stub fetch with a capture closure (no extra packages).
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

// T-009-05 (AC-009-1 client half): wire-shape rows (TA-4.2#14), most recent first.
const BASE_TIME = Date.parse('2026-10-31T12:00:00.000Z')

function makeRows(count: number, status: number, expiresOffsetMs: number): unknown[] {
  const rows: unknown[] = []
  for (let i = 0; i < count; i++) {
    rows.push({
      id: `${i.toString(16).padStart(2, '0')}${'0'.repeat(30)}`, // Guid 'n'-form (32 hex chars)
      name: `Row ${String(i + 1).padStart(2, '0')}`,            // most recent first (R01 = newest)
      fileCount: i === 0 ? 1 : 3,                               // row 0 pins the "1 file" singular form
      totalBytes: i === 0 ? 123456789 : 1024,
      status,
      expiresAtUtc: new Date(BASE_TIME + expiresOffsetMs).toISOString(),
      downloadsCount: i === 0 ? 87 : 0,                          // row 0 pins "87/100" (AC-009-1 client half)
      maxDownloads: i === 0 ? 100 : 10,
      recipientCount: 2,
    })
  }
  return rows
}

const BASE_PROFILE: StoredProfile = { userId: 'u1', email: 'a@b.c', displayName: 'A', theme: 'system', planCode: 'free' }

// Render App at /files BEFORE mounting (App reads pathname in its lazy useState init — push first).
// The fetch stub must be installed via stubFetch(...) by the caller before this runs.
// Returns a scoped host so EVERY query stays within THIS test's own DOM subtree — no cross-test leakage.
function renderAppAtFiles(): { host: HTMLElement; cleanup: () => void } {
  writeSession(BASE_PROFILE)
  window.history.pushState('', '', FILES_PATH)
  window.dispatchEvent(new PopStateEvent('popstate'))
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(App))
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
  clearSession()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  window.history.pushState('', '', '/')
})

describe('FilesPage (T-009-05)', () => {
  it('(a) PageOne_Renders25Rows_MostRecentFirst — 30 transfers → page 1 renders 25 rows, first/last in order, meta via formatBytes + "87/100"', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: makeRows(25, 1, 3600_000), nextCursor: 'CURSOR_PAGE2' }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      const rowEls = host.querySelectorAll('.mf-row')
      expect(rowEls.length).toBe(25)   // AC-009-1 client half: first page renders exactly 25 rows
      expect(rowEls[0].querySelector('.mf-name')!.getAttribute('title')).toBe('Row 01')   // most recent first
      expect(rowEls[24].querySelector('.mf-name')!.getAttribute('title')).toBe('Row 25')
      expect(captured.calls[0]).toContain('/transfers?status=all')
      expect(captured.calls[0]).not.toContain('cursor=')   // page 1 has no cursor param (TA-4.1.4)

      const firstMeta = rowEls[0].querySelector('.mf-meta')!.textContent!
      expect(firstMeta).toContain(formatBytes(123456789))   // TA-8.5 — size via the existing formatBytes, never hand-formatted
      expect(firstMeta).toContain('87/100')
      expect(firstMeta).toContain('1 file')                  // singular at 1
    } finally {
      cleanup()
    }
  })

  it('(b) Next_WalksRemaining5 — clicking Next loads the final 5 via the cursor and disables Next (TA-4.1.4 paging)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      const url = new URL(req.url, 'http://localhost:8080')
      if (url.searchParams.get('cursor') === 'CURSOR_PAGE2') {
        const last5 = Array.from({ length: 5 }, (_, j) => ({ ...makeRows(1, 1, 3600_000)[0] as object, id: `p2id-${j}`, name: `Page2 Row ${j + 1}` }))
        return new Response(JSON.stringify({ items: last5, nextCursor: null }), { status: 200 })
      }
      return new Response(JSON.stringify({ items: makeRows(25, 1, 3600_000), nextCursor: 'CURSOR_PAGE2' }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      expect(host.querySelectorAll('.mf-row').length).toBe(25)

      const nextBtn = host.querySelectorAll('.mf-next')[0] as HTMLButtonElement
      expect(nextBtn.disabled).toBe(false)   // a next page exists (exit check 1: Next walks the remaining 5)
      await act(async () => {
        nextBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      const rowEls = host.querySelectorAll('.mf-row')
      expect(rowEls.length).toBe(5)          // exactly the remaining 5 render (page REPLACES rows)
      expect(rowEls[0].querySelector('.mf-name')!.getAttribute('title')).toBe('Page2 Row 1')   // provably the 2nd page
      expect(captured.calls[1]).toContain('cursor=CURSOR_PAGE2')   // the opaque cursor is forwarded verbatim (TA-4.1.4)

      const nextBtnAfter = host.querySelectorAll('.mf-next')[0] as HTMLButtonElement
      expect(nextBtnAfter.disabled).toBe(true)   // nextCursor null → last page
    } finally {
      cleanup()
    }
  })

  it('(c) ActivePill_OnlyActiveRows_AndResetsCursor — clicking Active fetches status=active (no cursor) and renders only those rows', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      const url = new URL(req.url, 'http://localhost:8080')
      if (url.searchParams.get('status') === 'active') {
        return new Response(JSON.stringify({ items: [{ ...makeRows(1, 1, 3600_000)[0] as object, name: 'Only Active' }], nextCursor: null }), { status: 200 })
      }
      return new Response(JSON.stringify({ items: makeRows(25, 1, 3600_000), nextCursor: 'CURSOR_PAGE2' }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      const activePill = Array.from(host.querySelectorAll('.mf-pill')) as HTMLButtonElement[]
      const activeBtn = activePill.find((b) => b.textContent === 'Active')!
      await act(async () => {
        activeBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      // Exit check 2: the pill forwards its wire value to ?status= and resets paging (no cursor on re-fetch).
      expect(captured.calls.at(-1)!.includes('status=active')).toBe(true)
      expect(captured.calls.at(-1)).not.toContain('cursor=')
      const rowEls = host.querySelectorAll('.mf-row')
      expect(rowEls.length).toBe(1)   // only the active rows render
      expect(rowEls[0].querySelector('.mf-name')!.getAttribute('title')).toBe('Only Active')

      const ariaPill = Array.from(host.querySelectorAll('.mf-pill')) as HTMLButtonElement[]
      expect(ariaPill.find((b) => b.textContent === 'Active')!.getAttribute('aria-pressed')).toBe('true')
    } finally {
      cleanup()
    }
  })

  it('(d) ExpiredPill_IncludesDownloadLimit — status=expired seeds byte-2 + byte-3 rows → both render, chips "Expired" gray + "Download limit reached" amber', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      const url = new URL(req.url, 'http://localhost:8080')
      if (url.searchParams.get('status') === 'expired') {
        return new Response(JSON.stringify({
          items: [
            makeRows(1, 2, -3600_000)[0] as object,   // byte 2 → "Expired" gray
            makeRows(1, 3, -3600_000)[0] as object,   // byte 3 → "Download limit reached" amber (US-009-02)
          ],
          nextCursor: null,
        }), { status: 200 })
      }
      return new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      const expiredPill = Array.from(host.querySelectorAll('.mf-pill')) as HTMLButtonElement[]
      const expiredBtn = expiredPill.find((b) => b.textContent === 'Expired')!
      await act(async () => {
        expiredBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      expect(captured.calls.at(-1)!.includes('status=expired')).toBe(true)
      const rowEls = host.querySelectorAll('.mf-row')
      expect(rowEls.length).toBe(2)   // exit check 2: Expired includes download-limit rows
      const chips = Array.from(host.querySelectorAll('.mf-chip')) as HTMLElement[]
      expect(chips.some((c) => c.textContent === 'Expired' && c.className.includes('mf-chip-gray'))).toBe(true)
      expect(chips.some((c) => c.textContent === 'Download limit reached' && c.className.includes('mf-chip-amber'))).toBe(true)
    } finally {
      cleanup()
    }
  })

  it('(e) CountdownTicks_NoServerCalls — countdown ticks client-side from ExpiresAtUtc; expired rows show "Expired {date}" with ZERO extra fetches (AC-009-3)', async () => {
    const captured: { calls: string[] } = { calls: [] }

    vi.useFakeTimers()   // mocks setInterval so advanceTimersByTime fires the component's 1s tick.
    vi.setSystemTime(BASE_TIME + 3600_000)   // mount baseline: the row below expires at T+3660s (60s ahead) — live countdown on paint.

    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [{ ...makeRows(1, 1, 3660_000)[0] as object }], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      const initialFetchCount = captured.calls.length   // the initial fetch — nothing else is allowed (AC-009-3).
      expect(initialFetchCount).toBe(1)

      // Advance 65 fake seconds: advanceTimersByTime both fires the component's 1s interval and
      // moves Date.now() forward, so the client-side countdown recomputes with zero server calls.
      for (let i = 0; i < 65; i++) {
        await act(async () => { vi.advanceTimersByTime(1000) })
      }

      const rowEl = host.querySelectorAll('.mf-row')[0] as HTMLElement
      expect(rowEl.textContent).toContain('Expired')   // the countdown flipped to "Expired {date}" client-side.
      expect(rowEl.textContent).not.toContain('expires in')
      expect(captured.calls.length).toBe(initialFetchCount)   // no per-second server calls (AC-009-3 client half).
    } finally {
      cleanup()
    }
  })

  it('(f) LinkOnly_Transfer_ShowsLinkOnly — a recipientCount-0 row renders "Link only" in the meta (EC-009-4)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    const linkOnlyRow = { ...makeRows(1, 1, 3600_000)[0] as object, recipientCount: 0 }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [linkOnlyRow], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      expect(host.textContent).toContain('Link only')   // EC-009-4 — link-only transfer copy in the row
    } finally {
      cleanup()
    }
  })

  it('(g) ZeroTransfers_EmptyState_WithCtaToHome — items:[] → "No transfers yet." + "Send something" CTA pushes "/" (FR-009-5)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      expect(host.textContent).toContain('No transfers yet.')   // FR-009-5 empty state copy
      const ctaBtn = host.querySelectorAll('.mf-empty-cta')[0] as HTMLButtonElement
      expect(ctaBtn.textContent).toBe('Send something')

      await act(async () => {
        ctaBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })
      // CTA navigates home via App's goHome (T-008-03 pushState + popstate precedent) — pathname is the observable.
      expect(window.location.pathname).toBe('/')
    } finally {
      cleanup()
    }
  })

  it('(h) PrevDisabled_OnFirstPage_And_NextDisabled_OnLastPage — paging affordance states', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      const url = new URL(req.url, 'http://localhost:8080')
      if (url.searchParams.get('cursor') === 'CURSOR_PAGE2') {
        const last5 = Array.from({ length: 5 }, (_, j) => ({ ...makeRows(1, 1, 3600_000)[0] as object, id: `p2id-${j}`, name: `Page2 Row ${j + 1}` }))
        return new Response(JSON.stringify({ items: last5, nextCursor: null }), { status: 200 })
      }
      return new Response(JSON.stringify({ items: makeRows(25, 1, 3600_000), nextCursor: 'CURSOR_PAGE2' }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      // First page: Prev disabled at index 0 (TA-4.1.4).
      let prevBtn = host.querySelectorAll('.mf-prev')[0] as HTMLButtonElement
      expect(prevBtn.disabled).toBe(true)

      // Walk to the last page: Next disabled when nextCursor was null.
      let nextBtn = host.querySelectorAll('.mf-next')[0] as HTMLButtonElement
      await act(async () => {
        nextBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      prevBtn = host.querySelectorAll('.mf-prev')[0] as HTMLButtonElement
      nextBtn = host.querySelectorAll('.mf-next')[0] as HTMLButtonElement
      expect(prevBtn.disabled).toBe(false)   // page 2 has a previous page (the retry/prev affordance works)
      expect(nextBtn.disabled).toBe(true)
    } finally {
      cleanup()
    }
  })

  // ===== T-009-06 (FR-009-3, AC-009-2 client half, EC-009-2): row actions + delete confirm modal =====

  // Plan D7 fixture: fileCount=3, totalBytes=1_288_490_188 → formatBytes → "1.20 GB" (value<10 → toFixed(2) —
  // the REAL helper output is pinned here, NOT the spec's illustrative "1.2 GB").
  const T6_BASE_ROW: Record<string, unknown> = {
    ...makeRows(1, 1, 3600_000)[0] as object,
    id: 't6id', name: 'T6 row', fileCount: 3, totalBytes: 1288490188, linkId: 'ab3def5g',
  }

  it('(i) CopyLink_WritesExactUrl — clicking Copy writes {origin}/t/{linkId} via the clipboard (FR-009-3)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    // jsdom has no native clipboard API — the component must see a spy on navigator.clipboard.
    const writeTextSpy = vi.fn(async () => {})
    Object.defineProperty(window, 'navigator', {
      value: { ...window.navigator, clipboard: { writeText: writeTextSpy } },
      configurable: true,
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      const copyBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      expect(copyBtn.find((b) => b.getAttribute('aria-label') === 'Copy link')).toBeDefined()
      await act(async () => {
        (copyBtn.find((b) => b.getAttribute('aria-label') === 'Copy link') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      // Client-side URL construction — NO round-trip for the recipient URL (T-009-06 D4).
      expect(writeTextSpy).toHaveBeenCalledTimes(1)
      expect(writeTextSpy).toHaveBeenCalledWith(`${window.location.origin}/t/ab3def5g`)
    } finally {
      cleanup()
    }
  })

  it('(j) ViewLink_OpensRecipientUrl — clicking View opens /t/{linkId} in a new context (FR-009-3)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    // Capture the open() call args via a closure (jsdom's window.open is a no-op — we don't care about its return).
    const openCalls: Array<[string | URL, string?, string?]> = []
    vi.stubGlobal('open', (url: string | URL, target?: string, features?: string) => {
      openCalls.push([url, target, features])
      return null as unknown as Window
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      const viewBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      expect(viewBtn.find((b) => b.getAttribute('aria-label') === 'View')).toBeDefined()
      await act(async () => {
        (viewBtn.find((b) => b.getAttribute('aria-label') === 'View') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      // window.open called with the client-built URL — RecipientPage is a guest route (App.tsx branch).
      expect(openCalls.length).toBe(1)
      expect(openCalls[0][0]).toBe(`${window.location.origin}/t/ab3def5g`)
      expect(openCalls[0][1]).toBe('_blank')
      expect(openCalls[0][2]).toBe('noopener')
    } finally {
      cleanup()
    }
  })

  it('(k) ResendStub_OnlyStatusToast_NoFetch — the Re-send button shows the T-023 status toast, NO fetch (T-023 route stub)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      const initialCalls = captured.calls.length   // exactly the initial list fetch

      const resendBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      expect(resendBtn.find((b) => b.getAttribute('aria-label') === 'Re-send')).toBeDefined()
      await act(async () => {
        (resendBtn.find((b) => b.getAttribute('aria-label') === 'Re-send') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      // T-023 route stub: the endpoint ships in F-TRF-010 — nothing is sent today, only this status toast.
      expect(host.textContent).toContain('Re-send lands in T-023 (F-TRF-010).')
      expect(captured.calls.length).toBe(initialCalls)   // no fetch on the Re-send stub (the plan forbids one).
    } finally {
      cleanup()
    }
  })

  it('(l) DeleteModal_ExactCopy — opening the modal shows AC-009-2 copy built from ROW data + formatBytes', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()

      const deleteBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      expect(deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete')).toBeDefined()
      await act(async () => {
        (deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      // AC-009-2 exact copy: "{count} files, {formatBytes(totalBytes)}. This can't be undone."
      const modalEl = host.querySelector('.pf-modal') as HTMLElement
      expect(modalEl).not.toBeNull()
      const titleEl = modalEl.querySelector('.pf-modal-title') as HTMLElement
      const copyEl = modalEl.querySelector('.pf-modal-copy') as HTMLElement
      expect(titleEl.textContent).toBe('Delete transfer?')
      expect(copyEl.textContent).toBe(`3 files, ${formatBytes(1288490188)}. This can't be undone.`)
      expect(copyEl.textContent).toContain('1.20 GB')   // REAL formatBytes output (toFixed 2), not the spec's '1.2 GB'.
    } finally {
      cleanup()
    }
  })

  it('(m) ConfirmDelete_204_RowRemoved_ToastStatus — confirm on 204 removes the row + role=status "Transfer deleted."', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') {
        return new Response(null, { status: 204 })   // endpoint 17 returns NoContent with NO body on success.
      }
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      expect(host.querySelectorAll('.mf-row').length).toBe(1)

      const deleteBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      const confirmBtn = host.querySelector('.pf-modal-confirm') as HTMLButtonElement
      await act(async () => {
        confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()   // flush the DELETE's promise chain (the stub is async).
      })

      expect(captured.calls.some((c) => c.includes('/transfers/t6id'))).toBe(true)   // the DELETE was sent.
      expect(host.querySelectorAll('.mf-row').length).toBe(0)   // the row was removed locally on success.
      const toastEl = host.querySelector('[role="status"]') as HTMLElement
      expect(toastEl.textContent).toBe('Transfer deleted.')
      expect(host.querySelector('.pf-modal')).toBeNull()   // the modal closed.
    } finally {
      cleanup()
    }
  })

  it('(n) ConfirmDelete_404NotFound_EcSuccess — 404 with code NOT_FOUND is EC-009-2 success (no error, row gone)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') {
        return new Response(JSON.stringify({ code: 'NOT_FOUND', type: 'https://tools.ietf.org/html/rfc7807' }), { status: 404 })
      }
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      expect(host.querySelectorAll('.mf-row').length).toBe(1)

      const deleteBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      const confirmBtn = host.querySelector('.pf-modal-confirm') as HTMLButtonElement
      await act(async () => {
        confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
      })

      // EC-009-2: a second tab already deleted the transfer — no error, the row quietly vanishes.
      expect(host.querySelectorAll('.mf-row').length).toBe(0)   // removed locally (it's gone server-side).
      const toastEl = host.querySelector('[role="status"]') as HTMLElement
      expect(toastEl.textContent).toBe('Transfer deleted.')
      expect(host.querySelector('[role="alert"]')).toBeNull()   // no error region for EC-009-2.
    } finally {
      cleanup()
    }
  })

  it('(o) ConfirmDelete_500_ErrorAlert — a real failure keeps the modal open with a role=alert error (AC-009-2)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'DELETE') {
        return new Response(null, { status: 500 })   // a genuine failure (anything NOT the EC-009-2 success shape).
      }
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      expect(host.querySelectorAll('.mf-row').length).toBe(1)

      const deleteBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })

      const confirmBtn = host.querySelector('.pf-modal-confirm') as HTMLButtonElement
      await act(async () => {
        confirmBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
      })

      // Failure (anything else): inline role=alert error in the modal, modal stays open.
      const alertEl = host.querySelector('[role="alert"]') as HTMLElement
      expect(alertEl.textContent).toBe("Could not delete the transfer. Please try again.")
      expect(host.querySelector('.pf-modal')).not.toBeNull()   // the modal is still open.
      expect(host.querySelectorAll('.mf-row').length).toBe(1)   // the row is NOT removed on failure.
    } finally {
      cleanup()
    }
  })

  it('(p) CancelOrEscape_NoDeleteSent — closing the modal sends nothing (Cancel button + Escape key)', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(captured, () => {
      return new Response(JSON.stringify({ items: [T6_BASE_ROW], nextCursor: null }), { status: 200 })
    })

    const { host, cleanup } = renderAppAtFiles()
    try {
      await flush()
      const initialCalls = captured.calls.length   // exactly the initial list fetch — nothing else is allowed.

      const deleteBtn = Array.from(host.querySelectorAll('.mf-actions button')) as HTMLButtonElement[]
      await act(async () => {
        (deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })
      expect(host.querySelector('.pf-modal')).not.toBeNull()   // the modal is open.

      // Cancel button closes the modal — nothing is sent.
      const cancelBtn = host.querySelector('.pf-modal-cancel') as HTMLButtonElement
      await act(async () => {
        cancelBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })
      expect(host.querySelector('.pf-modal')).toBeNull()   // the modal closed.
      expect(captured.calls.length).toBe(initialCalls)     // nothing sent on Cancel.

      // Escape closes it too — still nothing sent.
      await act(async () => {
        (deleteBtn.find((b) => b.getAttribute('aria-label') === 'Delete') as HTMLButtonElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })
      expect(host.querySelector('.pf-modal')).not.toBeNull()   // the modal is open again.

      await act(async () => {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
      })
      expect(host.querySelector('.pf-modal')).toBeNull()       // Escape closed it (closeDelete — nothing sent).
      expect(captured.calls.length).toBe(initialCalls)         // still nothing sent on Escape.
    } finally {
      cleanup()
    }
  })
})
