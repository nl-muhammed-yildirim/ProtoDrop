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

/** T-003-03 (TA-4.1.3): endpoint 4's terminal Problem+JSON stub — codes pinned at wa.domain/Errors/ProblemCodes.cs. */
function stubFetchProblem(status: number, code: string, details: string): void {
  vi.stubGlobal(
    'fetch',
    async () =>
      new Response(JSON.stringify({ type: 'https://example.com/errors/transfer-expired', title: 'Transfer expired', status, code, details }), {
        status,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
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
// T-003-03: the shared terminal branch — expired (410/404) and download-limit (410).
const isTerminal = (): boolean => useRecipientStore.getState().status === 'expired' || useRecipientStore.getState().status === 'downloadLimit'

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

  it('shows the error state for a 410 response without a terminal code (non-Problem bodies stay generic)', async () => {
    // No 'code' field — not one of TA-4.1.3's terminal codes, so the store keeps the generic error shape.
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

  it('renders the expired screen for 410 with sender email in details (AC-003-3)', async () => {
    stubFetchProblem(410, 'TRANSFER_EXPIRED', 'This transfer has expired. Contact alice@example.com for a new link.')

    const host = await renderApp(isTerminal)

    expect(useRecipientStore.getState().status).toBe('expired')
    // §4.6 shape: icon, one line (server details verbatim), one action; no file list or From heading.
    const message = 'This transfer has expired. Contact alice@example.com for a new link.'
    expect(host.querySelector('.rp-heading')?.textContent).toBe(message)
    const action = host.querySelector('.rp-terminal-action') as HTMLAnchorElement
    expect(action.textContent).toBe('Send something')
    expect(action.getAttribute('href')).toBe('/')
    expect(host.querySelectorAll('.rp-row')).toHaveLength(0)
    host.remove()
  })

  it('renders the identical screen for 404 (unknown linkId) — only HTTP status differs (AC-003-4, FR-003-9)', async () => {
    // Both bodies are email-less (the server never sends an email for NotFound — ExpiredDetails(null)),
    // so the rendered .rp-terminal section must be byte-identical across 410 and 404.
    const details = 'This transfer has expired. Contact the sender for a new link.'
    stubFetchProblem(410, 'TRANSFER_EXPIRED', details)
    const host410 = await renderApp(isTerminal)
    expect(host410.querySelector('.rp-terminal')).not.toBeNull()
    const html410 = host410.querySelector('.rp-terminal')!.outerHTML
    await act(async () => { useRecipientStore.getState().reset() })

    stubFetchProblem(404, 'TRANSFER_EXPIRED', details)
    const host404 = await renderApp(isTerminal)
    expect(host404.querySelector('.rp-terminal')).not.toBeNull()
    expect(host404.querySelector('.rp-terminal')!.outerHTML).toBe(html410) // byte-identical — no ID enumeration
    expect(host404.querySelectorAll('.rp-row')).toHaveLength(0)
    host410.remove()
    host404.remove()
  })

  it('renders the download-limit screen for 410 MAX_DOWNLOADS_REACHED with sender email (AC-003-5)', async () => {
    stubFetchProblem(410, 'MAX_DOWNLOADS_REACHED', 'All downloads have been used. Contact alice@example.com for a new link.')

    const host = await renderApp(isTerminal)

    expect(useRecipientStore.getState().status).toBe('downloadLimit')
    expect(host.querySelector('.rp-heading')?.textContent).toBe('All downloads have been used. Contact alice@example.com for a new link.')
    const action = host.querySelector('.rp-terminal-action') as HTMLAnchorElement
    expect(action.textContent).toBe('Send something')
    expect(host.querySelectorAll('.rp-row')).toHaveLength(0)
    host.remove()
  })

  it('renders the download-limit screen without a fabricated address when email is null (AC-003-5 alt flow)', async () => {
    stubFetchProblem(410, 'MAX_DOWNLOADS_REACHED', 'All downloads have been used. Contact the sender for a new link.')

    const host = await renderApp(isTerminal)

    // Details render verbatim — the screen must not contain a fabricated address.
    expect(host.querySelector('.rp-heading')?.textContent).toBe('All downloads have been used. Contact the sender for a new link.')
    expect((host.textContent ?? '').includes('@')).toBe(false)
    host.remove()
  })

  it('byte-leak audit: no file list, no sizes, no extra requests on any terminal screen (TA-4.2#4)', async () => {
    const cases: Array<[number, string, string]> = [
      [410, 'TRANSFER_EXPIRED', 'This transfer has expired. Contact alice@example.com for a new link.'],
      [404, 'TRANSFER_EXPIRED', 'This transfer has expired. Contact the sender for a new link.'],
      [410, 'MAX_DOWNLOADS_REACHED', 'All downloads have been used. Contact alice@example.com for a new link.'],
    ]

    for (const [status, code, details] of cases) {
      const calls: string[] = []
      vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
        calls.push(String(input))
        return new Response(JSON.stringify({ code, details }), { status, headers: { 'Content-Type': 'application/problem+json' } })
      })

      const host = await renderApp(isTerminal)

      expect(host.querySelectorAll('.rp-row')).toHaveLength(0) // no file list
      expect(host.querySelectorAll('.rp-size')).toHaveLength(0) // no sizes
      expect(host.querySelectorAll('.rp-download, .rp-download-all')).toHaveLength(0) // no Download controls — nothing to mint a SAS for
      expect(calls).toEqual([`http://localhost:8080/api/v1/public/transfers/${LINK_ID}`]) // exactly one fetch — endpoint 4 only
      host.remove()
      await act(async () => { useRecipientStore.getState().reset() })
    }
  })

  it('shows "Downloads left: N" under the list for finite caps (FR-003-5)', async () => {
    stubFetch(200, activeBody({ downloadsLeft: 86 }))
    const host = await renderApp(isActive)

    expect(host.querySelector('.rp-downloads-left')?.textContent).toBe('Downloads left: 86')
    host.remove()
    await act(async () => { useRecipientStore.getState().reset() })

    // Edge of the visibility rule — one remaining still shows (0 is already the terminal limit screen).
    stubFetch(200, activeBody({ downloadsLeft: 1 }))
    const host2 = await renderApp(isActive)

    expect(host2.querySelector('.rp-downloads-left')?.textContent).toBe('Downloads left: 1')
    host2.remove()
  })

  it('hides "Downloads left" for Business (∞) — downloadsLeft null', async () => {
    stubFetch(200, activeBody({ downloadsLeft: null }))

    const host = await renderApp(isActive)

    expect(host.querySelector('.rp-downloads-left')).toBeNull()
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
    expect(matchLinkPath('/t/tab3def5')).toBe('tab3def5') // t is minted (T-003-02-fix: reviewer blocker)
    expect(matchLinkPath('/t/vab3def5')).toBe('vab3def5') // v is minted (T-003-02-fix: reviewer blocker)
    expect(matchLinkPath('/')).toBeNull()
    expect(matchLinkPath('/t/')).toBeNull()
    expect(matchLinkPath('/t/Ab3dEf5g')).toBeNull() // uppercase never minted (LinkId.cs charset)
    expect(matchLinkPath('/t/abcdefio12')).toBeNull() // i/l/o/u are excluded from the charset
    expect(matchLinkPath('/t/ab3def5gH')).toBeNull() // 9 chars
    expect(matchLinkPath('/t/ab3def5g/')).toBeNull() // trailing slash
    expect(matchLinkPath('/t/ab3def5')).toBeNull() // 7 chars
  })

  it('linkIds containing t or v route to the active recipient page, not the landing page (T-003-02-fix)', async () => {
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        calls.push(String(input))
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    // 8-char linkIds — the reviewer's /t/tab3def5g example is 9 chars (invalid), so use tab3def5 / vab3def5.
    for (const linkId of ['tab3def5', 'vab3def5']) {
      // App reads window.location.pathname on mount — render a fresh tree per linkId (beforeEach's pushState is overwritten).
      window.history.pushState('', '', `/t/${linkId}`)
      const host = await renderApp(isActive)
      expect(host.querySelector('.rp-heading')?.textContent).toBe('From: Studio Nova')
      expect(host.querySelector('.dz-title')).toBeNull() // not the landing page
      expect(calls.at(-1)).toBe(`http://localhost:8080/api/v1/public/transfers/${linkId}`) // endpoint 4 got the t/v linkId
      host.remove()
      // The root is still subscribed (afterEach unmounts), so reset lands in an act scope — same contract as the reset test below.
      await act(async () => { useRecipientStore.getState().reset() })
    }
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
