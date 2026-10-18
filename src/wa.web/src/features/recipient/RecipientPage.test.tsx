// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import App from '../../App'
import { useRecipientStore } from './RecipientStore'
import { matchLinkPath } from './recipientRoute'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

const LINK_ID = 'ab3def5g'

// T-003-02 (TA-4.2#4): endpoint 4's exact wire shape — property names pinned at wa.api/Program.cs.
interface PublicTransferBody {
  from: string
  note: string | null
  files: Array<{ fileId: string; name: string; sizeBytes: number }>
  hasDownloadAll: boolean
  downloadsLeft: number | null
  passwordRequired: boolean
}

// Live roots are tracked so afterEach can unmount them — React 18 warns on store updates hitting
// a still-subscribed tree (the suite shares one module-level Zustand store).
const roots: Array<ReturnType<typeof createRoot>> = []

/** Stub fetch with a per-test body (same per-file stub convention as LinkScreen.test — no cross-file coupling). */
function stubFetch(status: number, body: PublicTransferBody): void {
  vi.stubGlobal(
    'fetch',
    async () =>
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
  )
}

/**
 * Renders <App /> and, in the same act scope, polls the store until settled() holds. The recipient page's
 * load() resolves through the stub fetch in a few microtask turns — React 18 requires those store updates to
 * be flushed inside an act scope, so render + wait travel together (the async-store-load pattern).
 */
async function renderApp(settled: () => boolean): Promise<HTMLDivElement> {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  roots.push(root)
  await act(async () => {
    void root.render(createElement(App))
    for (let i = 0; i < 400 && !settled(); i++) {
      await Promise.resolve()
    }
  })
  return host
}

const isActive = (): boolean => useRecipientStore.getState().status === 'active'
const isError = (): boolean => useRecipientStore.getState().status === 'error'

const activeBody = (overrides?: Partial<PublicTransferBody>): PublicTransferBody => ({
  from: 'Studio Nova',
  note: 'Final renders — please review',
  files: [
    { fileId: 'f1', name: 'render.mp4', sizeBytes: 2_147_483_648 },
    { fileId: 'f2', name: 'poster.png', sizeBytes: 5_242_880 },
  ],
  hasDownloadAll: true,
  downloadsLeft: 9,
  passwordRequired: false,
  ...overrides,
})

describe('RecipientPage (App at /t/{linkId} — T-003-02)', () => {
  beforeEach(() => {
    // pushState before render: App reads the pathname on mount (plan §2 test convention).
    window.history.pushState('', '', `/t/${LINK_ID}`)
  })

  afterEach(() => {
    for (const root of roots.splice(0)) act(() => { root.unmount() }) // cleanup runs reset() inside act.
    useRecipientStore.getState().reset() // aborts any in-flight load and restores the loading shape.
    window.history.replaceState(null, '', '/')
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it(`renders "From: {senderName}" for an active transfer (AC-003-1)`, async () => {
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        calls.push(String(input))
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isActive)

    expect(host.querySelector('.rp-heading')?.textContent).toBe('From: Studio Nova')
    // Endpoint 4 was hit with the linkId from the URL — no other URLs fetched.
    expect(calls).toEqual([`http://localhost:8080/api/v1/public/transfers/${LINK_ID}`])
    host.remove()
  })

  it('renders both file names with human-readable sizes in row order (AC-003-1)', async () => {
    stubFetch(200, activeBody())

    const host = await renderApp(isActive)

    const rows = Array.from(host.querySelectorAll('.rp-row'))
    expect(rows).toHaveLength(2)
    // formatBytes(2147483648) = "2.00 GB"; formatBytes(5242880) = "5.00 MB" — 2 decimals below 10 units.
    expect(rows[0].querySelector('.rp-name')?.textContent).toBe('render.mp4')
    expect(rows[0].querySelector('.rp-size')?.textContent).toBe('2.00 GB')
    expect(rows[1].querySelector('.rp-name')?.textContent).toBe('poster.png')
    expect(rows[1].querySelector('.rp-size')?.textContent).toBe('5.00 MB')
    host.remove()
  })

  it('each file row has a Download control with aria-label "Download {name}, {size}"', async () => {
    stubFetch(200, activeBody())

    const host = await renderApp(isActive)

    const buttons = Array.from(host.querySelectorAll('.rp-download'))
    expect(buttons).toHaveLength(2)
    expect(buttons[0]).toBeInstanceOf(HTMLButtonElement)
    expect(buttons[0].getAttribute('aria-label')).toBe('Download render.mp4, 2.00 GB')
    expect(buttons[1].getAttribute('aria-label')).toBe('Download poster.png, 5.00 MB')
    host.remove()
  })

  it('shows Download all when the transfer has >= 2 files and clicking it does not fetch (AC-003-1)', async () => {
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        calls.push(String(input))
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isActive)

    const all = host.querySelector('.rp-download-all') as HTMLButtonElement
    expect(all).not.toBeNull()
    expect(all.textContent).toBe('Download all')
    const countBefore = calls.length

    await act(async () => {
      all.click()
      // Placeholder handler — no navigation, no fetch (T-003-06 / F-TRF-004 wire it).
      await Promise.resolve()
    })

    expect(calls.length).toBe(countBefore) // page view + click minted nothing (TA-4.2#4)
    host.remove()
  })

  it('hides Download all for a single-file transfer', async () => {
    stubFetch(200, activeBody({
      files: [{ fileId: 'f1', name: 'render.mp4', sizeBytes: 2_147_483_648 }],
      hasDownloadAll: false,
    }))

    const host = await renderApp(isActive)

    expect(host.querySelector('.rp-download-all')).toBeNull()
    // The per-file Download row stays.
    expect(host.querySelectorAll('.rp-download')).toHaveLength(1)
    host.remove()
  })

  it('renders the note as a blockquote when present', async () => {
    stubFetch(200, activeBody())

    const host = await renderApp(isActive)

    const note = host.querySelector('.rp-note') as HTMLElement | null
    expect(note).not.toBeNull()
    expect(note!.tagName).toBe('BLOCKQUOTE')
    expect(note!.textContent).toBe('Final renders — please review')
    host.remove()
  })

  it('omits the note blockquote when note is null', async () => {
    stubFetch(200, activeBody({ note: null }))

    const host = await renderApp(isActive)

    expect(host.querySelector('.rp-note')).toBeNull()
    host.remove()
  })

  it('shows no sign-up prompt, banner or modal in the DOM (AC-003-1 / FR-003-2)', async () => {
    stubFetch(200, activeBody())

    const host = await renderApp(isActive)

    expect(/sign up|create account/i.test(host.textContent ?? '')).toBe(false)
    host.remove()
  })

  it('renders the loading state before the fetch resolves', async () => {
    // Never-resolving fetch: the page must stay in its loading shape.
    vi.stubGlobal('fetch', () => new Promise<never>(() => {}))

    const host = await renderApp(() => useRecipientStore.getState().status === 'loading')

    expect(host.querySelector('.rp-loading')?.getAttribute('role')).toBe('status')
    expect(host.textContent).toContain('Loading…')
    expect(host.querySelectorAll('.rp-row')).toHaveLength(0)
    host.remove()
  })

  it('shows the error state for a 410 response (T-003-03 will replace with dedicated screens)', async () => {
    vi.stubGlobal(
      'fetch',
      async () =>
        new Response(JSON.stringify({ type: 'about:blank', title: 'Gone', status: 410 }), {
          status: 410,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    )

    const host = await renderApp(isError)

    expect(host.querySelector('.rp-error')?.textContent).toBe('Something went wrong')
    expect(host.querySelectorAll('.rp-row')).toHaveLength(0) // no file leak on the error state
    expect(/sign up|create account/i.test(host.textContent ?? '')).toBe(false)
    host.remove()
  })

  it('a fetch() rejection (network down) lands in the error state, not an unhandled rejection', async () => {
    vi.stubGlobal('fetch', () => Promise.reject(new TypeError('Failed to fetch')))

    const host = await renderApp(isError)

    expect(host.querySelector('.rp-error')?.textContent).toBe('Something went wrong')
    host.remove()
  })

  it('landing at "/" is unchanged and /t/ swaps in the recipient page (App wiring)', async () => {
    stubFetch(200, activeBody())

    // Contrast: fresh pathname read per render — App reads window.location.pathname on mount.
    window.history.replaceState(null, '', '/')
    const landing = await renderApp(() => true)
    expect(landing.querySelector('.dz-title')?.textContent).toBe('Send your files.')
    expect(landing.querySelector('.rp')).toBeNull()
    landing.remove()

    window.history.replaceState(null, '', `/t/${LINK_ID}`)
    const recipient = await renderApp(isActive)
    expect(recipient.querySelector('.rp-heading')?.textContent).toBe('From: Studio Nova')
    expect(recipient.querySelector('.dz-title')).toBeNull()
    recipient.remove()
  })

  it('matchLinkPath accepts only /t/[8-char Crockford base32 charset] and extracts the linkId', () => {
    expect(matchLinkPath('/t/ab3def5g')).toBe('ab3def5g')
    expect(matchLinkPath('/')).toBeNull()
    expect(matchLinkPath('/t/')).toBeNull()
    expect(matchLinkPath('/t/Ab3dEf5g')).toBeNull() // uppercase never minted (LinkId.cs charset)
    expect(matchLinkPath('/t/abcdefio12')).toBeNull() // i/l/o/u are excluded from the charset
    expect(matchLinkPath('/t/ab3def5gH')).toBeNull() // 9 chars
    expect(matchLinkPath('/t/ab3def5g/')).toBeNull() // trailing slash
    expect(matchLinkPath('/t/ab3def5')).toBeNull() // 7 chars
  })

  it('reset restores the loading shape (store contract for test hermeticity)', async () => {
    stubFetch(200, activeBody())

    const host = await renderApp(isActive)
    await act(async () => {
      useRecipientStore.getState().reset() // the store update must land inside an act scope (root still subscribed).
    })

    const s = useRecipientStore.getState()
    expect(s.status).toBe('loading')
    expect(s.files).toEqual([])
    expect(s.from).toBe('')
    expect(s.note).toBeNull()
    host.remove()
  })
})
