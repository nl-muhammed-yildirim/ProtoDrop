// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AdminFlagsPage from './AdminFlagsPage'

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
  captured: { calls: string[]; bodies: string[] },
  responder?: (req: Request) => Response | Promise<Response>,
): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const url = typeof input === 'string' ? input : String(input)
      captured.calls.push(url)
      captured.bodies.push(typeof init?.body === 'string' ? init.body : '')
      const req = new Request(url, { method: init?.method ?? 'GET', body: init?.body as string | undefined })
      return responder ? await responder(req) : new Response(null, { status: 200 })
    },
  )
}

// T-011-13 (FR-011-2 / FR-011-5): endpoint-23 wire rows — one key pinning a JSON value; endpoint-25
// audit-recent row pinning actorEmail/action/entityType/entityId.
const FLAG_KEY = 'feature.ads'
const FLAG_VALUE = '{"enabled":false}'
const AUDIT_ROW = {
  id: 1,
  actorEmail: 'admin@x.com',
  action: 'flag.set',
  entityType: 'FeatureFlag',
  entityId: FLAG_KEY,
  detailsJson: null,
  createdAtUtc: '2026-10-31T12:00:00.000Z',
}

function flagsResponder(req: Request): Response {
  if (req.method === 'PUT') return new Response(null, { status: 204 })   // endpoint 23 PUT success.
  if (req.url.endsWith('/admin/flags')) {
    return new Response(JSON.stringify([{ key: FLAG_KEY, value: FLAG_VALUE }]), { status: 200 })
  }
  return new Response(JSON.stringify([AUDIT_ROW]), { status: 200 })   // /admin/audit-recent.
}

// Render the page component directly (AdminShell's role narrowing is covered by AdminShell.test.tsx).
function renderPage(role: 'SuperAdmin' | 'Operator'): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(AdminFlagsPage, { role }))
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

describe('AdminFlagsPage (T-011-13 / FR-011-2, FR-011-5)', () => {
  it('Rows_RenderKeyAndJsonPreview — key + the --fs-small monospace preview of the current value', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, flagsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const rowCells = host.querySelectorAll('.ad-table tbody tr')[0].querySelectorAll('td')
      expect(rowCells[0].textContent).toBe(FLAG_KEY)   // the key renders.
      const previewEl = host.querySelector('.ad-json-preview') as HTMLElement
      expect(previewEl.textContent).toBe(FLAG_VALUE)   // the current value, read-only.
      const textarea = host.querySelector('textarea.ad-filter-input') as HTMLTextAreaElement
      expect(textarea.value).toBe(FLAG_VALUE)          // the editable raw string starts at the wire value.
    } finally {
      cleanup()
    }
  })

  it('Save_SendsRawBodyVerbatim_NotJsonStringified — AC-011-2: PUT body is the raw string exactly', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, flagsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const saveBtn = host.querySelector('.ad-row-actions button') as HTMLButtonElement
      await act(async () => {
        saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      const putCall = captured.calls.find((c) => c.endsWith(`/admin/flags/${FLAG_KEY}`))
      expect(putCall).toBeDefined()   // the PUT was sent to /admin/flags/{key}.
      const putIndex = captured.calls.indexOf(putCall as string)
      expect(captured.bodies[putIndex]).toBe(FLAG_VALUE)   // raw string verbatim — NOT JSON.stringify'd.
    } finally {
      cleanup()
    }
  })

  it('Save_204_RefetchesFlagsAndAuditRecent — AC-011-2 end-to-end: after 204 both GETs re-run', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, flagsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()
      const initialFlagsGets = captured.calls.filter((c) => c.endsWith('/admin/flags')).length
      const initialAuditGets = captured.calls.filter((c) => c.endsWith('/admin/audit-recent')).length

      const saveBtn = host.querySelector('.ad-row-actions button') as HTMLButtonElement
      await act(async () => {
        saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      // After the 204, BOTH GETs re-run (the panel must show the new audit row).
      expect(captured.calls.filter((c) => c.endsWith('/admin/flags')).length).toBe(initialFlagsGets + 1)
      expect(captured.calls.filter((c) => c.endsWith('/admin/audit-recent')).length).toBe(initialAuditGets + 1)
    } finally {
      cleanup()
    }
  })

  it('BadJson_400Validation_ShowsInlineInvalidJson_AndOldValueStaysLive — EC-011-4', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'PUT') return new Response(null, { status: 400 })   // bad JSON → 400 VALIDATION.
      return flagsResponder(req)
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const saveBtn = host.querySelector('.ad-row-actions button') as HTMLButtonElement
      await act(async () => {
        saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      const alertEl = host.querySelector('[role="alert"]') as HTMLElement
      expect(alertEl.textContent).toBe('Invalid JSON')   // EC-011-4 inline copy.
      const textarea = host.querySelector('textarea.ad-filter-input') as HTMLTextAreaElement
      expect(textarea.value).toBe(FLAG_VALUE)            // the old value stays live in the field.
    } finally {
      cleanup()
    }
  })

  it('Save_Failure_RendersInlineAlert_AndOldValueStaysLive — any non-204 failure', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, (req) => {
      if (req.method === 'PUT') return new Response(null, { status: 500 })   // a genuine failure.
      return flagsResponder(req)
    })

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const saveBtn = host.querySelector('.ad-row-actions button') as HTMLButtonElement
      await act(async () => {
        saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await Promise.resolve()
        await Promise.resolve()
      })

      const alertEl = host.querySelector('[role="alert"]') as HTMLElement
      expect(alertEl.textContent).toBe('Could not save the flag. Please try again.')   // role=alert.
      const textarea = host.querySelector('textarea.ad-filter-input') as HTMLTextAreaElement
      expect(textarea.value).toBe(FLAG_VALUE)            // the old value stays live in the field.
    } finally {
      cleanup()
    }
  })

  it('AuditPanel_RendersActorActionTargetAndTimeInFsSmall — FR-011-5: all four columns render', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, flagsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      // The Recent actions panel is the SECOND .ad-table on this screen.
      const tables = host.querySelectorAll('.ad-table')
      expect(tables.length).toBe(2)
      const auditRowCells = tables[1].querySelectorAll('tbody tr')[0].querySelectorAll('td')
      expect(auditRowCells[0].textContent).toBe(AUDIT_ROW.actorEmail)   // Actor.
      expect(auditRowCells[1].textContent).toBe(AUDIT_ROW.action)       // Action.
      expect(auditRowCells[2].textContent).toBe(`${AUDIT_ROW.entityType} ${AUDIT_ROW.entityId}`)   // Target.
      const d = new Date(AUDIT_ROW.createdAtUtc)
      expect(auditRowCells[3].textContent).toContain(
        d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
      )   // Time — formatCreatedAt (en-US), --fs-small via .ad-table tbody td.
    } finally {
      cleanup()
    }
  })

  it('Operator_Role_SaveDisabled_WithTooltip_NotHidden — AC-011-3: Save present, disabled=true, title set', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, flagsResponder)

    const { host, cleanup } = renderPage('Operator')
    try {
      await flush()

      const saveBtns = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(saveBtns.length).toBe(1)   // the Save button is present in the DOM (NOT hidden).
      for (const btn of saveBtns) {
        expect(btn.disabled).toBe(true)   // Operator → disabled.
        expect(btn.getAttribute('title')).toBe('SuperAdmin only')   // tooltip is set.
      }
    } finally {
      cleanup()
    }
  })

  it('SuperAdmin_Role_SaveEnabled_NoTooltip — SuperAdmin → enabled, no tooltip', async () => {
    const captured: { calls: string[]; bodies: string[] } = { calls: [], bodies: [] }
    stubFetch(captured, flagsResponder)

    const { host, cleanup } = renderPage('SuperAdmin')
    try {
      await flush()

      const saveBtns = Array.from(host.querySelectorAll('.ad-row-actions button')) as HTMLButtonElement[]
      expect(saveBtns.length).toBe(1)
      for (const btn of saveBtns) {
        expect(btn.disabled).toBe(false)   // SuperAdmin → enabled.
        expect(btn.getAttribute('title')).toBeNull()   // no tooltip needed.
      }
    } finally {
      cleanup()
    }
  })
})
