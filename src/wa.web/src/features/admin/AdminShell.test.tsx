// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AdminShell from './AdminShell'
import type { AdminScreen } from './adminRoute'
import { formatBytes } from '../../core/upload/formatBytes'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// jsdom has no localStorage by default — install an in-memory shim (FilesPage.test.tsx pattern).
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

// Hand-stub fetch with a capture closure — routes /auth/me and /admin/overview separately.
function stubFetch(
  captured: { calls: string[] },
  authMeResponse: Response | (() => Response),
  overviewResponse: Response | (() => Response),
): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL): Promise<Response> => {
      const url = typeof input === 'string' ? input : String(input)
      captured.calls.push(url)
      if (url.includes('/auth/me')) {
        return typeof authMeResponse === 'function' ? authMeResponse() : authMeResponse
      }
      if (url.includes('/admin/overview')) {
        return typeof overviewResponse === 'function' ? overviewResponse() : overviewResponse
      }
      return new Response(null, { status: 200 })
    },
  )
}

const SNAPSHOT_ISO = '2026-11-05T14:30:00.000Z'

function makeOverviewPayload(overrides?: Partial<Record<string, unknown>>): string {
  const base = {
    activeTransfers: 42,
    storageBytesUsed: 123456789,
    newUsersToday: 7,
    emailsSent7d: 100,
    emailsFailed7d: 0,
    snapshotAtUtc: SNAPSHOT_ISO,
    recentTransfers: [
      { linkId: 'abc12345', ownerEmail: 'a@b.c', status: 1, totalBytes: 1024, createdAtUtc: SNAPSHOT_ISO },
      { linkId: 'def67890', ownerEmail: null, status: 3, totalBytes: 2048, createdAtUtc: SNAPSHOT_ISO },
    ],
  }
  return JSON.stringify({ ...base, ...overrides })
}

function renderShell(screen: AdminScreen): { host: HTMLElement; cleanup: () => void } {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(createElement(AdminShell, { screen }))
  })
  return { host, cleanup: () => { root.unmount(); host.remove() } }
}

async function flush(): Promise<void> {
  await act(async () => { await Promise.resolve() })
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

describe('AdminShell (T-011-10)', () => {
  it('Overview_RendersFourStatCards_WithLiveCounts', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'superadmin' }), { status: 200 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()

      const values = Array.from(host.querySelectorAll('.ad-card-value')).map((el) => el.textContent?.trim())
      expect(values).toContain('42')
      expect(values).toContain(formatBytes(123456789)) // storage card via formatBytes (TA-8.5)
      expect(values).toContain('7')
      expect(values).toContain('100 / 0')
    } finally {
      cleanup()
    }
  })

  it('Overview_ShowsUpdatedTimeLine_FromSnapshotAtUtc', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'superadmin' }), { status: 200 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()

      const updatedEl = host.querySelector('.ad-updated')
      expect(updatedEl).not.toBeNull()
      expect(updatedEl!.textContent).toContain('Updated')
      // The snapshot time is stated — derived from SNAPSHOT_ISO via formatUpdatedAt.
      const d = new Date(SNAPSHOT_ISO)
      expect(updatedEl!.textContent).toContain(d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }))
    } finally {
      cleanup()
    }
  })

  it('RoleBadge_MatchesSessionRole_ForSuperadmin', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'superadmin' }), { status: 200 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()
      expect(host.querySelector('.ad-badge')!.textContent).toBe('SuperAdmin')
    } finally {
      cleanup()
    }
  })

  it('RoleBadge_MatchesSessionRole_ForOperator', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'operator' }), { status: 200 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()
      expect(host.querySelector('.ad-badge')!.textContent).toBe('Operator')
    } finally {
      cleanup()
    }
  })

  it('NonAdmin_Forbidden_RendersNotAdminScreen', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ code: 'FORBIDDEN' }), { status: 403 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()
      expect(host.querySelector('.ad-prompt-h1')!.textContent).toBe('Not an admin')
      // No cards rendered.
      expect(host.querySelectorAll('.ad-card').length).toBe(0)
    } finally {
      cleanup()
    }
  })

  it('NotSignedIn_401_RendersSignInPrompt', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ code: 'UNAUTHENTICATED' }), { status: 401 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()
      expect(host.querySelector('.ad-prompt-h1')!.textContent).toBe('Sign in to continue')
    } finally {
      cleanup()
    }
  })

  it('FailedEmails_PositiveCount_RendersDangerClass', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'superadmin' }), { status: 200 }),
      new Response(makeOverviewPayload({ emailsFailed7d: 2 }), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()
      const dangerEl = host.querySelector('.ad-danger')
      expect(dangerEl).not.toBeNull()
      expect(dangerEl!.textContent).toContain('2')
    } finally {
      cleanup()
    }
  })

  it('RecentTransfersTable_RendersRows_FromEndpoint20', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'superadmin' }), { status: 200 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()

      const rows = host.querySelectorAll('.ad-table tbody tr')
      expect(rows.length).toBe(2)

      // Row 1: linkId, owner email, status label, size, created.
      const row1Cells = Array.from(rows[0].querySelectorAll('td'))
      expect(row1Cells[0].textContent).toBe('abc12345')
      expect(row1Cells[1].textContent).toBe('a@b.c')
      expect(row1Cells[2].textContent).toBe('Active')
      expect(row1Cells[3].textContent).toBe('1.00 KB') // formatBytes(1024)

      // Row 2: ownerEmail null → "—", status 3 → "Download limit reached".
      const row2Cells = Array.from(rows[1].querySelectorAll('td'))
      expect(row2Cells[0].textContent).toBe('def67890')
      expect(row2Cells[1].textContent).toBe('—')
      expect(row2Cells[2].textContent).toBe('Download limit reached')
    } finally {
      cleanup()
    }
  })

  it('LeftNav_HasAllFiveItems_InOrder', async () => {
    const captured: { calls: string[] } = { calls: [] }
    stubFetch(
      captured,
      new Response(JSON.stringify({ role: 'superadmin' }), { status: 200 }),
      new Response(makeOverviewPayload(), { status: 200 }),
    )

    const { host, cleanup } = renderShell('overview')
    try {
      await flush()

      const items = Array.from(host.querySelectorAll('.ad-nav-item')).map((el) => el.textContent?.trim())
      expect(items).toEqual(['Overview', 'Transfers', 'Users', 'Flags', 'Suppressions'])
    } finally {
      cleanup()
    }
  })
})
