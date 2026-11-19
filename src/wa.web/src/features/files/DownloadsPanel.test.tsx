// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { DownloadsPanel } from './DownloadsPanel'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

interface Stub { calls: string[] }

function stubApi(opts: { allowed: boolean; analytics?: { status: number; body?: unknown } }): Stub {
  const stub: Stub = { calls: [] }
  vi.stubGlobal('fetch', async (input: RequestInfo | URL): Promise<Response> => {
    const url = typeof input === 'string' ? input : String(input)
    stub.calls.push(url)
    if (url.endsWith('/billing/plan')) return new Response(JSON.stringify({ features: { analytics: opts.allowed } }), { status: 200 })
    const a = opts.analytics ?? { status: 200, body: {} }
    return new Response(JSON.stringify(a.body ?? {}), { status: a.status })
  })
  return stub
}

const flush = async (): Promise<void> => {
  for (let i = 0; i < 4; i++) await act(async () => { await Promise.resolve() })
}

function render(status = 1): HTMLDivElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => { root.render(createElement(DownloadsPanel, { transferId: 't-1', status })) })
  return host
}

const DATA = {
  total: 6,
  daily: [{ date: '2026-11-17', count: 1 }, { date: '2026-11-18', count: 2 }, { date: '2026-11-19', count: 3 }],
  countries: [{ code: 'NL', count: 4 }, { code: '??', count: 2 }],
  browsers: [{ family: 'Chrome', count: 5 }, { family: 'Other', count: 1 }],
  files: [
    { fileId: 'f1', name: 'report.pdf', count: 3 },
    { fileId: 'f2', name: 'photo.png', count: 1 },
    { fileId: 'f3', name: 'notes.txt', count: 0 },
  ],
  downloadAll: 2,
  topFiles: [
    { fileId: 'f1', name: 'report.pdf', count: 3 },
    { fileId: 'f2', name: 'photo.png', count: 1 },
  ],
}

afterEach(() => { vi.unstubAllGlobals(); document.body.innerHTML = '' })

describe('DownloadsPanel', () => {
  it('Free_ShowsProRequired_NoAnalyticsFetch (AC-023-3)', async () => {
    const stub = stubApi({ allowed: false })
    const host = render()
    await flush()

    expect(host.textContent).toContain('Pro plan required')
    expect(host.querySelector('a')!.getAttribute('href')).toBe('/account/plan')
    expect(stub.calls.some((c) => c.includes('/analytics'))).toBe(false)
  })

  it('Pro_WithData_ShowsPolylineAndThreeChips', async () => {
    stubApi({ allowed: true, analytics: { status: 200, body: DATA } })
    const host = render()
    await flush()

    const poly = host.querySelector('svg[role="img"] polyline')!
    expect(poly.getAttribute('points')).toBe('0,39.33 150,20.67 300,2')
    expect(poly.getAttribute('fill')).toBe('none')
    const chips = Array.from(host.querySelectorAll('.mf-dl-chip')).map((c) => c.textContent)
    expect(chips).toHaveLength(3)
    expect(chips[0]).toContain('6')
    expect(chips[1]).toContain('Netherlands')
    expect(chips[2]).toContain('Chrome')
  })

  it('Pro_ZeroDownloads_ShowsEmptyLineOnly', async () => {
    stubApi({ allowed: true, analytics: { status: 200, body: { total: 0, daily: [{ date: '2026-11-19', count: 0 }], countries: [], browsers: [] } } })
    const host = render()
    await flush()

    expect(host.textContent).toContain('No downloads yet.')
    expect(host.querySelector('svg')).toBeNull()
    expect(host.querySelectorAll('.mf-dl-chip')).toHaveLength(0)
  })

  it('Pro_403_ShowsProRequired (EC-023-6)', async () => {
    stubApi({ allowed: true, analytics: { status: 403 } })
    const host = render()
    await flush()

    expect(host.textContent).toContain('Pro plan required')
    expect(host.querySelector('svg')).toBeNull()
  })

  it('Pro_500_ShowsInlineError', async () => {
    stubApi({ allowed: true, analytics: { status: 500 } })
    const host = render()
    await flush()

    expect(host.querySelector('[role="alert"]')).not.toBeNull()
  })

  it('Expired_ShowsBadge (EC-023-5)', async () => {
    stubApi({ allowed: true, analytics: { status: 200, body: DATA } })
    const host = render(2)
    await flush()

    expect(host.querySelector('.mf-chip.mf-chip-gray')!.textContent).toBe('Expired')
    expect(host.querySelector('polyline')).not.toBeNull()
  })

  it('UnknownCountry_ShowsUnknown (EC-023-2)', async () => {
    stubApi({ allowed: true, analytics: { status: 200, body: { ...DATA, countries: [{ code: '??', count: 6 }] } } })
    const host = render()
    await flush()

    expect(host.querySelectorAll('.mf-dl-chip')[1].textContent).toContain('Unknown')
  })

  it('Pro_WithData_ShowsPerFileTable_DownloadAllRow', async () => {
    stubApi({ allowed: true, analytics: { status: 200, body: DATA } })
    const host = render()
    await flush()

    const rows = Array.from(host.querySelectorAll('table.mf-dl-files tbody tr'))
    expect(rows).toHaveLength(4)
    expect(rows[0].textContent).toBe('report.pdf3')
    expect(rows[2].textContent).toBe('notes.txt0')
    const zip = host.querySelectorAll('tr.mf-dl-zip')
    expect(zip).toHaveLength(1)
    expect(zip[0].textContent).toContain('Download all')
    expect(zip[0].textContent).toContain('2')
  })

  it('Pro_SingleFile_NoDownloadAllRow', async () => {
    stubApi({ allowed: true, analytics: { status: 200, body: { ...DATA, files: [DATA.files[0]], downloadAll: null } } })
    const host = render()
    await flush()

    expect(host.querySelectorAll('table.mf-dl-files tbody tr')).toHaveLength(1)
    expect(host.querySelector('.mf-dl-zip')).toBeNull()
  })

  it('Pro_TopDownloads_RankedList_MaxFive', async () => {
    const top = Array.from({ length: 5 }, (_, i) => ({ fileId: `t${i}`, name: `file${i}.bin`, count: 9 - i }))
    stubApi({ allowed: true, analytics: { status: 200, body: { ...DATA, topFiles: top } } })
    const host = render()
    await flush()

    const items = Array.from(host.querySelectorAll('ol.mf-dl-top li'))
    expect(items).toHaveLength(5)
    expect(items.map((li) => li.textContent)).toEqual(['file0.bin 9', 'file1.bin 8', 'file2.bin 7', 'file3.bin 6', 'file4.bin 5'])
    expect(host.querySelector('ol.mf-dl-top')!.textContent).not.toContain('Download all')
    expect(host.textContent).toContain('Top downloads')
  })
})
