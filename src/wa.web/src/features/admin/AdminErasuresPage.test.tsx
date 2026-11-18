// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import AdminErasuresPage from './AdminErasuresPage'
import type { ErasureRowDto } from './erasuresData'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

function stubFetch(calls: string[], responder: (url: string) => Response): void {
  vi.stubGlobal('fetch', async (input: RequestInfo | URL): Promise<Response> => {
    const url = typeof input === 'string' ? input : String(input)
    calls.push(url)
    return responder(url)
  })
}

function makeRow(i: number, over: Partial<ErasureRowDto> = {}): ErasureRowDto {
  return {
    userId: String(i).padStart(32, '0'),
    email: `d****@x${i}.com`,
    deletedAtUtc: new Date(Date.now() - 60 * 60 * 1000).toISOString(),
    reHomedTransferCount: i,
    sweepStatus: 'pending',
    sweptAtUtc: null,
    ...over,
  }
}

function json(rows: ErasureRowDto[], nextCursor: string | null): Response {
  return new Response(JSON.stringify({ items: rows, nextCursor }), { status: 200 })
}

function renderPage(): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(AdminErasuresPage))
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

async function flush(): Promise<void> {
  for (let i = 0; i < 6; i++) await act(async () => { await Promise.resolve() })
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AdminErasuresPage (US-047-03 / FR-047-6)', () => {
  it('RendersRows_MaskedEmail_Count_PendingAndSwept', async () => {
    const swept = makeRow(2, { sweepStatus: 'swept', sweptAtUtc: '2026-11-18T03:00:00Z', reHomedTransferCount: 0, email: null })
    stubFetch([], () => json([makeRow(1, { reHomedTransferCount: 3 }), swept], null))
    const { host, cleanup } = renderPage()
    try {
      await flush()
      const rows = host.querySelectorAll('.ad-table tbody tr')
      expect(rows.length).toBe(2)
      const c0 = rows[0].querySelectorAll('td')
      expect(c0[0].textContent).toBe('d****@x1.com')
      expect(c0[2].textContent).toBe('3')
      expect(c0[3].textContent).toBe('Pending')
      const c1 = rows[1].querySelectorAll('td')
      expect(c1[0].textContent).toBe('—')
      expect(c1[2].textContent).toBe('0')
      expect(c1[3].textContent).toMatch(/^Swept /)
    } finally {
      cleanup()
    }
  })

  it('PendingOlderThanTwoDays_HasLaggingClass', async () => {
    const old = makeRow(1, { deletedAtUtc: new Date(Date.now() - 49 * 3600 * 1000).toISOString() })
    stubFetch([], () => json([old, makeRow(2)], null))
    const { host, cleanup } = renderPage()
    try {
      await flush()
      const rows = host.querySelectorAll('.ad-table tbody tr')
      expect(rows[0].classList.contains('ad-row-lagging')).toBe(true)
      expect(rows[1].classList.contains('ad-row-lagging')).toBe(false)
    } finally {
      cleanup()
    }
  })

  it('NextPage_UsesNextCursor', async () => {
    const calls: string[] = []
    stubFetch(calls, (url) => (url.includes('cursor=c2') ? json([makeRow(2)], null) : json([makeRow(1)], 'c2')))
    const { host, cleanup } = renderPage()
    try {
      await flush()
      await act(async () => { (host.querySelector('.mf-next') as HTMLButtonElement).click() })
      await flush()
      expect(calls.at(-1)).toContain('cursor=c2')
      expect(host.querySelector('.ad-table tbody td')?.textContent).toBe('d****@x2.com')
    } finally {
      cleanup()
    }
  })

  it('ExportCsv_FetchesEveryPage_CsvHasAllRows', async () => {
    const calls: string[] = []
    stubFetch(calls, (url) => (url.includes('cursor=c2')
      ? json([makeRow(3)], null)
      : json([makeRow(1), makeRow(2)], 'c2')))
    const blobs: Blob[] = []
    vi.stubGlobal('URL', Object.assign(URL, {
      createObjectURL: (b: Blob) => { blobs.push(b); return 'blob:x' },
      revokeObjectURL: () => undefined,
    }))
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const { host, cleanup } = renderPage()
    try {
      await flush()
      const exportBtn = Array.from(host.querySelectorAll('button')).find((b) => b.textContent === 'Export CSV') as HTMLButtonElement
      await act(async () => { exportBtn.click() })
      await flush()
      expect(calls.length).toBe(3) // initial page + two export pages
      expect(blobs.length).toBe(1)
      const text = await blobs[0].text()
      const lines = text.trim().split('\r\n')
      expect(lines.length).toBe(4) // header + 3 rows (every row, not just the visible page)
      expect(lines[0]).toBe('userId,email,deletedAtUtc,reHomedTransferCount,sweepStatus,sweptAtUtc')
    } finally {
      cleanup()
    }
  })

  it('LoadFailure_ShowsAlert_NoToast', async () => {
    stubFetch([], () => new Response(null, { status: 500 }))
    const { host, cleanup } = renderPage()
    try {
      await flush()
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('Could not load erasures.')
      expect(host.querySelector('[role="status"]')).toBeNull()
    } finally {
      cleanup()
    }
  })

  it('NoWriteControls', async () => {
    stubFetch([], () => json([makeRow(1)], 'c2'))
    const { host, cleanup } = renderPage()
    try {
      await flush()
      const labels = Array.from(host.querySelectorAll('button')).map((b) => b.textContent)
      expect(labels).toEqual(['Export CSV', 'Previous', 'Next'])
    } finally {
      cleanup()
    }
  })
})
