// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import App from '../../App'
import { useUploadEngine } from '../../core/upload/UploadEngine'
import { useRecipientStore } from './RecipientStore'
import { matchLinkPath } from './recipientRoute'
import { growthTelemetryLog } from './growthTelemetry'

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

describe('RecipientPage (T-003-05 — password gate UI)', () => {
  const gatedBody = (): PublicTransferBody => activeBody({ passwordRequired: true })

  // Dispatching stub: POST /unlock → { token }; GET endpoint 4 gated when the URL lacks ?t=, ungated with ?t=tok123.
  function stubDispatchingFetch(): void {
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL, init?: RequestInit) => {
        if ((init?.method ?? '').toUpperCase() === 'POST') {
          return new Response(JSON.stringify({ token: 'tok123' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        const url = String(input)
        const hasToken = url.includes('?t=tok123')
        return new Response(JSON.stringify(activeBody({ passwordRequired: !hasToken })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    )
  }

  const isGatedActive = (): boolean => useRecipientStore.getState().status === 'active' && useRecipientStore.getState().passwordRequired === true
  const isUngatedActive = (): boolean => useRecipientStore.getState().status === 'active' && !useRecipientStore.getState().passwordRequired

  /** Fills the controlled password input (LinkScreen.test native value setter idiom) and dispatches React's input event. */
  function typePassword(host: HTMLDivElement, text: string): void {
    const input = host.querySelector('#rp-password-input') as HTMLInputElement
    const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
    set.call(input, text)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  }

  beforeEach(() => {
    window.history.pushState('', '', `/t/${LINK_ID}`)
  })

  afterEach(() => {
    for (const root of roots.splice(0)) act(() => { root.unmount() }) // cleanup runs reset() inside act.
    useRecipientStore.getState().reset()
    window.sessionStorage.clear()
    window.history.replaceState(null, '', '/')
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('gated render shows only the password card — no file list, no sizes, one fetch (exit check 1 + no-byte-leak)', async () => {
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        calls.push(String(input))
        return new Response(JSON.stringify(gatedBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isGatedActive)

    expect(host.querySelector('.rp-heading')?.textContent).toBe('From: Studio Nova')
    // FR-003-6 byte-leak audit — nothing beyond sender name + form.
    expect(host.querySelectorAll('.rp-row')).toHaveLength(0)
    expect(host.querySelectorAll('.rp-size')).toHaveLength(0)
    expect(host.querySelectorAll('.rp-note')).toHaveLength(0)
    expect(host.querySelectorAll('.rp-download, .rp-download-all')).toHaveLength(0)
    expect(host.querySelectorAll('.rp-downloads-left')).toHaveLength(0)
    // Exactly endpoint 4 — no unlock call on mount.
    expect(calls).toEqual([`http://localhost:8080/api/v1/public/transfers/${LINK_ID}`])
    host.remove()
  })

  it('field shape: single current-password input, toggle, full-width primary submit disabled when empty (AC-003-2)', async () => {
    stubFetch(200, gatedBody())

    const host = await renderApp(isGatedActive)

    const inputs = Array.from(host.querySelectorAll('input'))
    expect(inputs).toHaveLength(1)
    expect(inputs[0].getAttribute('type')).toBe('password')
    expect(inputs[0].getAttribute('autocomplete')).toBe('current-password')

    const toggle = host.querySelector('.rp-password-toggle') as HTMLButtonElement
    expect(toggle.getAttribute('aria-label')).toBe('Show password')

    let submit = host.querySelector('.rp-password-submit') as HTMLButtonElement
    expect(submit.classList.contains('btn-primary')).toBe(true)
    expect(submit.textContent).toBe('Unlock')
    expect(submit.disabled).toBe(true) // empty value → disabled

    await act(async () => {
      typePassword(host, 'secret')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    submit = host.querySelector('.rp-password-submit') as HTMLButtonElement
    expect(submit.disabled).toBe(false) // enabled after typing
    host.remove()
  })

  it('toggle flips type password→text→password and updates aria-label/aria-pressed each click', async () => {
    stubFetch(200, gatedBody())

    const host = await renderApp(isGatedActive)
    const input = host.querySelector('#rp-password-input') as HTMLInputElement
    const toggle = host.querySelector('.rp-password-toggle') as HTMLButtonElement

    expect(input.getAttribute('type')).toBe('password')
    expect(toggle.getAttribute('aria-pressed')).toBe('false')

    await act(async () => { toggle.click() })
    expect(input.getAttribute('type')).toBe('text')
    expect(toggle.getAttribute('aria-label')).toBe('Hide password')
    expect(toggle.getAttribute('aria-pressed')).toBe('true')

    await act(async () => { toggle.click() })
    expect(input.getAttribute('type')).toBe('password')
    expect(toggle.getAttribute('aria-label')).toBe('Show password')
    expect(toggle.getAttribute('aria-pressed')).toBe('false')
    host.remove()
  })

  it('correct password → token in sessionStorage, ?t= re-query, file list renders (exit check 2 + AC-003-2)', async () => {
    const calls: Array<{ url: string; method: string; body?: unknown }> = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input)
        const hasToken = url.includes('?t=tok123')
        calls.push({ url, method: (init?.method ?? 'GET').toUpperCase(), body: init?.body })
        if ((init?.method ?? '').toUpperCase() === 'POST') {
          return new Response(JSON.stringify({ token: 'tok123' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        return new Response(JSON.stringify(activeBody({ passwordRequired: !hasToken })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    )

    const host = await renderApp(isGatedActive)
    await act(async () => {
      typePassword(host, 'secret')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    await act(async () => {
      (host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      for (let i = 0; i < 400 && !isUngatedActive(); i++) {
        await Promise.resolve()
      }
    })

    expect(host.querySelectorAll('.rp-row')).toHaveLength(2)
    expect(window.sessionStorage.getItem(`pd.unlock.${LINK_ID}`)).toBe('tok123')
    const unlockCalls = calls.filter((c) => c.url.endsWith('/unlock'))
    expect(unlockCalls).toHaveLength(1) // exactly one POST
    expect(JSON.parse(String(unlockCalls[0].body))).toEqual({ password: 'secret' })
    const get4 = calls.filter((c) => c.method === 'GET')
    expect(get4.at(-1)?.url).toBe(`http://localhost:8080/api/v1/public/transfers/${LINK_ID}?t=tok123`) // re-query carries ?t=
    host.remove()
  })

  it('same-tab refresh: seeded sessionStorage → ungated in exactly one fetch, no re-prompt (AC-003-2)', async () => {
    window.sessionStorage.setItem(`pd.unlock.${LINK_ID}`, 'tok123')
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        calls.push(String(input))
        const url = String(input)
        return new Response(JSON.stringify(activeBody({ passwordRequired: !url.includes('?t=tok123') })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    )

    const host = await renderApp(isUngatedActive)

    expect(host.querySelectorAll('.rp-row')).toHaveLength(2)
    // Exactly one fetch — endpoint 4 WITH ?t=; the gate never rendered, no unlock POST.
    expect(calls).toEqual([`http://localhost:8080/api/v1/public/transfers/${LINK_ID}?t=tok123`])
    host.remove()
  })

  it('wrong password → shake + role=alert "Wrong password", still gated, no token (AC-003-2)', async () => {
    vi.stubGlobal(
      'fetch',
      async (_input: RequestInfo | URL, init?: RequestInit) => {
        if ((init?.method ?? '').toUpperCase() === 'POST') {
          return new Response(JSON.stringify({ type: 'https://example.com/errors/wrong-password', title: 'Wrong password', status: 401, code: 'WRONG_PASSWORD', details: 'Wrong password' }), {
            status: 401,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        }
        return new Response(JSON.stringify(gatedBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isGatedActive)
    await act(async () => {
      typePassword(host, 'nope')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    await act(async () => {
      ;(host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      while (!useRecipientStore.getState().unlockError) {
        await Promise.resolve()
      }
    })

    expect(host.querySelector('.rp-password-row')!.className).toContain('rp-shake')
    const alert = host.querySelector('.rp-password-error') as HTMLParagraphElement
    expect(alert.getAttribute('role')).toBe('alert')
    expect(alert.textContent).toBe('Wrong password')
    expect(useRecipientStore.getState().status).toBe('active') // still gated — no lockout, retry stays live.
    expect(useRecipientStore.getState().passwordRequired).toBe(true)
    expect(host.querySelectorAll('.rp-row')).toHaveLength(0)
    expect(window.sessionStorage.getItem(`pd.unlock.${LINK_ID}`)).toBeNull()
    expect((host.querySelector('#rp-password-input') as HTMLInputElement).value).toBe('nope') // input preserved
    host.remove()
  })

  it('a 401 omitting details falls back to client copy "Wrong password"', async () => {
    vi.stubGlobal(
      'fetch',
      async (_input: RequestInfo | URL, init?: RequestInit) => {
        if ((init?.method ?? '').toUpperCase() === 'POST') {
          return new Response(JSON.stringify({ type: 'https://example.com/errors/wrong-password', title: 'Wrong password', status: 401, code: 'WRONG_PASSWORD' }), {
            status: 401,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        }
        return new Response(JSON.stringify(gatedBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isGatedActive)
    await act(async () => {
      typePassword(host, 'nope')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    await act(async () => {
      ;(host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      while (!useRecipientStore.getState().unlockError) {
        await Promise.resolve()
      }
    })

    expect((host.querySelector('.rp-password-error') as HTMLParagraphElement).textContent).toBe('Wrong password') // fallback copy
    host.remove()
  })

  it('no lockout: retry after a wrong password succeeds in the same mounted tree (AC-003-2)', async () => {
    let posts = 0
    vi.stubGlobal(
      'fetch',
      async (_input: RequestInfo | URL, init?: RequestInit) => {
        const method = (init?.method ?? '').toUpperCase()
        if (method === 'POST') {
          posts++
          if (posts === 1) {
            return new Response(JSON.stringify({ code: 'WRONG_PASSWORD', details: 'Wrong password' }), {
              status: 401,
              headers: { 'Content-Type': 'application/problem+json' },
            })
          }
          return new Response(JSON.stringify({ token: 'tok123' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        const url = String(_input)
        return new Response(JSON.stringify(activeBody({ passwordRequired: !url.includes('?t=tok123') })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    )

    const host = await renderApp(isGatedActive)

    // First attempt — wrong password.
    await act(async () => {
      typePassword(host, 'wrong')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    await act(async () => {
      ;(host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      while (!useRecipientStore.getState().unlockError) {
        await Promise.resolve()
      }
    })
    expect(host.querySelector('.rp-password-error')?.textContent).toBe('Wrong password')

    // Second attempt — correct. Same tree, no remount.
    await act(async () => {
      typePassword(host, 'secret')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    const form = host.querySelector('.rp-password-card') as HTMLFormElement
    await act(async () => {
      form.requestSubmit()
      for (let i = 0; i < 400 && !isUngatedActive(); i++) {
        await Promise.resolve()
      }
    })

    expect(host.querySelectorAll('.rp-row')).toHaveLength(2)
    expect(host.querySelector('.rp-password-card')).toBeNull() // the gate is gone — file list renders.
    host.remove()
  })

  it('D6 precedence: terminal TRANSFER_EXPIRED from unlock beats the gate (T-003-03 wins)', async () => {
    const details = 'This transfer has expired. Contact alice@example.com for a new link.'
    vi.stubGlobal(
      'fetch',
      async (_input: RequestInfo | URL, init?: RequestInit) => {
        const method = (init?.method ?? '').toUpperCase()
        if (method === 'POST') {
          return new Response(JSON.stringify({ code: 'TRANSFER_EXPIRED', details }), {
            status: 410,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        }
        return new Response(JSON.stringify(gatedBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isGatedActive)
    await act(async () => {
      typePassword(host, 'secret')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    // The unlock POST returns the terminal 410 — poll the terminal status (the store never re-queries on terminal).
    const isTerminal = (): boolean => useRecipientStore.getState().status === 'expired' || useRecipientStore.getState().status === 'downloadLimit'
    await act(async () => {
      ;(host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      for (let i = 0; i < 400 && !isTerminal(); i++) {
        await Promise.resolve()
      }
    })

    expect(useRecipientStore.getState().status).toBe('expired')
    expect(host.querySelector('.rp-heading')?.textContent).toBe(details) // terminal screen wins — details verbatim
    expect(host.querySelectorAll('#rp-password-input')).toHaveLength(0) // no password input left in the DOM
    host.remove()
  })

  it('EC-003-2 tab isolation: cleared sessionStorage re-prompts at the same URL', async () => {
    stubDispatchingFetch()

    // First "tab" — unlock writes the token.
    const host = await renderApp(isGatedActive)
    await act(async () => {
      typePassword(host, 'secret')
      await Promise.resolve() // flush the controlled input's setValue update inside an act scope.
    })
    await act(async () => {
      ;(host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      for (let i = 0; i < 400 && !isUngatedActive(); i++) {
        await Promise.resolve()
      }
    })
    expect(host.querySelectorAll('.rp-row')).toHaveLength(2)
    expect(window.sessionStorage.getItem(`pd.unlock.${LINK_ID}`)).toBe('tok123')
    host.remove()

    // Simulate a second tab: unmount + cleared sessionStorage (same URL — beforeEach's pushState is re-asserted here).
    await act(async () => { useRecipientStore.getState().reset() })
    window.sessionStorage.clear()
    window.history.pushState('', '', `/t/${LINK_ID}`)

    const host2 = await renderApp(isGatedActive)
    expect(host2.querySelector('#rp-password-input')).not.toBeNull() // gate renders again — re-prompted by design
    expect(host2.querySelectorAll('.rp-row')).toHaveLength(0)
    host2.remove()
  })
})

describe('RecipientPage (T-003-06 — per-file download wiring)', () => {
  const SAS_URL = 'https://azurite.example.com/transfers/fake-sas?sig=abc'

  // Dispatching stub: endpoint 4 → active body; endpoint 6 → minted { url, expiresAt } (TA-4.2#6 wire shape).
  function stubDownloadFetch(status = 200): void {
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/download-url')) {
          return new Response(JSON.stringify({ url: SAS_URL, expiresAt: '2030-01-01T00:30:00Z' }), { status, headers: { 'Content-Type': 'application/json' } })
        }
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )
  }

  beforeEach(() => {
    window.history.pushState('', '', `/t/${LINK_ID}`)
  })

  afterEach(() => {
    for (const root of roots.splice(0)) act(() => { root.unmount() })
    useRecipientStore.getState().reset()
    window.sessionStorage.clear()
    window.history.replaceState(null, '', '/')
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('Download click → endpoint 6 fetch + browser navigation to the minted SAS (exit check: direct streaming)', async () => {
    // jsdom can't navigate — capture window.location.href via a fake getter so the store's
    // `window.location.href = body.url` lands in our setter instead of jsdom's "Not implemented" error.
    const realLocation = window.location as unknown
    const currentPathname: string | undefined = (realLocation as Location).pathname
    const fakeLocation: Record<string, unknown> = {}
    let assignedHref: string | null = null
    Object.defineProperty(fakeLocation, 'href', {
      configurable: true,
      get: () => '',
      set: (v: string) => { assignedHref = v },
    })
    // Keep the current pathname so App's matchLinkPath still sees the /t/{linkId} route while mounted.
    Object.defineProperty(fakeLocation, 'pathname', { configurable: true, get: () => currentPathname })
    Object.defineProperty(window, 'location', { configurable: true, get: () => fakeLocation })
    try {
      stubDownloadFetch()

      const host = await renderApp(isActive)
      const btn = host.querySelector('.rp-download') as HTMLButtonElement
      await act(async () => {
        btn.click() // user action → store.download (the endpoint 6 fetch lands inside this act scope).
        for (let i = 0; i < 400 && assignedHref === null; i++) { await Promise.resolve() }
      })

      expect(assignedHref).toBe(SAS_URL) // browser navigates to the minted SAS — direct streaming, no blob fetch.
      // The store still shows active with the same files (no terminal state).
      expect(useRecipientStore.getState().status).toBe('active')
      host.remove()
    } finally {
      Object.defineProperty(window, 'location', { configurable: true, value: realLocation })
    }
  })

  it('410 TRANSFER_EXPIRED from endpoint 6 → terminal expired screen (D6 precedence)', async () => {
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/download-url')) {
          return new Response(JSON.stringify({ code: 'TRANSFER_EXPIRED', details: 'This transfer has expired.' }), { status: 410, headers: { 'Content-Type': 'application/problem+json' } })
        }
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isActive)
    const btn = host.querySelector('.rp-download') as HTMLButtonElement
    await act(async () => {
      btn.click()
      for (let i = 0; i < 400 && useRecipientStore.getState().status !== 'expired'; i++) { await Promise.resolve() }
    })

    expect(useRecipientStore.getState().status).toBe('expired') // terminal screen wins — D6 precedence.
    host.remove()
  })

  it('503 INTERNAL from endpoint 6 → row-level "File temporarily unavailable" (US-003-03 alt flow)', async () => {
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/download-url')) {
          return new Response(JSON.stringify({ code: 'INTERNAL', details: 'Internal server error' }), { status: 503, headers: { 'Content-Type': 'application/problem+json' } })
        }
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isActive)
    const btn = host.querySelector('.rp-download') as HTMLButtonElement
    await act(async () => {
      btn.click()
      for (let i = 0; i < 400 && useRecipientStore.getState().downloadErrorFileId !== null; i++) { await Promise.resolve() }
    })

    expect(useRecipientStore.getState().status).toBe('active') // page still works — row-level error only.
    expect(host.querySelector('.rp-download-error')?.textContent).toBe('File temporarily unavailable.')
    host.remove()
  })

  it('in-flight download shows "Downloading…" and disables the button (UI notes: no blocking UI)', async () => {
    // Never-resolving fetch for endpoint 6 — the in-flight state persists.
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/download-url')) return new Promise<never>(() => {}) // never resolves
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    const host = await renderApp(isActive)
    const btn = host.querySelector('.rp-download') as HTMLButtonElement
    await act(async () => {
      btn.click()
      for (let i = 0; i < 400 && useRecipientStore.getState().downloadingFileId !== null; i++) { await Promise.resolve() }
    })

    expect(useRecipientStore.getState().downloadingFileId).toBe('f1') // the first file's fileId.
    const btnAfter = host.querySelector('.rp-download') as HTMLButtonElement
    expect(btnAfter.textContent).toBe('Downloading…') // in-flight indicator on the row.
    expect(btnAfter.disabled).toBe(true) // button disabled while in flight (the known minor all-rows-disabled behavior).
    host.remove()
  })
})

describe('RecipientPage (T-003-07 — growth loop)', () => {
  const SAS_URL = 'https://azurite.example.com/transfers/fake-sas?sig=abc'

  // Dispatching stub: endpoint 4 → active body; endpoint 6 → minted { url, expiresAt } (TA-4.2#6 wire shape).
  function stubDownloadFetch(): void {
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/download-url')) {
          return new Response(JSON.stringify({ url: SAS_URL, expiresAt: '2030-01-01T00:30:00Z' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        return new Response(JSON.stringify(activeBody()), { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )
  }

  /**
   * jsdom can't navigate — fake window.location.href via a configurable getter (same idiom as the T-003-06 download test).
   * pathname still reads through to jsdom's real history so the CTA's pushState('/') is seen by App's popstate handler.
   */
  function stubLocation(): { restore: () => void } {
    const realLocation = window.location as unknown
    const fakeLocation: Record<string, unknown> = {}
    Object.defineProperty(fakeLocation, 'href', {
      configurable: true,
      get: () => '',
      // Navigation captured — nothing to record for the growth CTA tests.
      set() { /* no-op */ },
    })
    const real = window.location as unknown as Location
    Object.defineProperty(fakeLocation, 'pathname', { configurable: true, get: () => real.pathname })
    Object.defineProperty(window, 'location', { configurable: true, get: () => fakeLocation })
    return { restore: () => Object.defineProperty(window, 'location', { configurable: true, value: realLocation }) }
  }

  /** Downloads every file row in order until the store reports them all completed (the full-download path). */
  async function downloadAllFiles(host: HTMLDivElement): Promise<void> {
    const total = useRecipientStore.getState().files.length
    let downloaded = 0
    while (downloaded < total) {
      const buttons = Array.from(host.querySelectorAll('.rp-download')) as HTMLButtonElement[]
      await act(async () => {
        buttons[downloaded].click()
        for (let i = 0; i < 400 && useRecipientStore.getState().downloadedFileIds.length < downloaded + 1; i++) { await Promise.resolve() }
      })
      downloaded += 1
    }
  }

  beforeEach(() => {
    window.history.pushState('', '', `/t/${LINK_ID}`)
    growthTelemetryLog.splice(0) // module-level record — keep tests hermetic.
  })

  afterEach(() => {
    for (const root of roots.splice(0)) act(() => { root.unmount() }) // cleanup runs reset() inside act.
    useRecipientStore.getState().reset()
    useUploadEngine.getState().reset() // seeded staging rows must not leak across tests.
    window.sessionStorage.clear()
    window.history.replaceState(null, '', '/')
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('full download (both files) → panel visible + exactly one growth_cta_shown post-download (AC full-download)', async () => {
    const loc = stubLocation()
    try {
      stubDownloadFetch()

      const host = await renderApp(isActive)
      // f1 — a partial download: no panel yet.
      const first = host.querySelector('.rp-download') as HTMLButtonElement
      await act(async () => {
        first.click()
        for (let i = 0; i < 400 && useRecipientStore.getState().downloadedFileIds.length < 1; i++) { await Promise.resolve() }
      })
      expect(host.querySelector('.rp-growth')).toBeNull() // partial — the page stays in its normal state.

      // f2 — completes the full download.
      const second = host.querySelectorAll('.rp-download')[1] as HTMLButtonElement
      await act(async () => {
        second.click()
        for (let i = 0; i < 400 && useRecipientStore.getState().downloadedFileIds.length < 2; i++) { await Promise.resolve() }
      })

      const panel = host.querySelector('.rp-growth')
      expect(panel).not.toBeNull()
      expect((panel as HTMLElement).querySelector('button')?.textContent).toBe('Send something')
      const shownPostDownload = growthTelemetryLog.filter((e) => e.event === 'growth_cta_shown' && e.source === 'post-download')
      expect(shownPostDownload).toHaveLength(1) // exactly one — the panel announced once.
      host.remove()
    } finally {
      loc.restore()
    }
  })

  it('partial download (1 of 5 files) → no panel, zero telemetry (EC partial)', async () => {
    const loc = stubLocation()
    try {
      vi.stubGlobal(
        'fetch',
        async (input: RequestInfo | URL) => {
          const url = String(input)
          if (url.includes('/download-url')) {
            return new Response(JSON.stringify({ url: SAS_URL, expiresAt: '2030-01-01T00:30:00Z' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
          }
          const body = activeBody({
            files: ['f1', 'f2', 'f3', 'f4', 'f5'].map((id) => ({ fileId: id, name: `${id}.bin`, sizeBytes: 1024 })),
          })
          return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
        },
      )

      const host = await renderApp(isActive)
      const btn = host.querySelector('.rp-download') as HTMLButtonElement
      await act(async () => {
        btn.click()
        for (let i = 0; i < 400 && useRecipientStore.getState().downloadedFileIds.length < 1; i++) { await Promise.resolve() }
      })

      expect(host.querySelector('.rp-growth')).toBeNull() // 1 of 5 — the panel must NOT appear.
      expect(growthTelemetryLog).toHaveLength(0) // zero telemetry events at all.
      host.remove()
    } finally {
      loc.restore()
    }
  })

  it('re-download after completion → panel stays, growth_cta_shown still exactly one (EC no re-announce)', async () => {
    const loc = stubLocation()
    try {
      stubDownloadFetch()

      const host = await renderApp(isActive)
      await downloadAllFiles(host) // f1 + f2 → full download.
      expect(host.querySelector('.rp-growth')).not.toBeNull()
      expect(growthTelemetryLog.filter((e) => e.event === 'growth_cta_shown' && e.source === 'post-download')).toHaveLength(1)

      // Re-download f1 — the panel never hides and never re-announces.
      const first = host.querySelector('.rp-download') as HTMLButtonElement
      await act(async () => {
        first.click()
        // The dedupe keeps downloadedFileIds.length at 2 — poll for the settled state (no in-flight row) instead.
        for (let i = 0; i < 400 && useRecipientStore.getState().downloadingFileId !== null; i++) { await Promise.resolve() }
        for (let i = 0; i < 10; i++) { await Promise.resolve() } // flush: store update → React commit.
      })
      expect(host.querySelector('.rp-growth')).not.toBeNull() // still visible.
      expect(growthTelemetryLog.filter((e) => e.event === 'growth_cta_shown' && e.source === 'post-download')).toHaveLength(1) // still exactly one.
      host.remove()
    } finally {
      loc.restore()
    }
  })

  it('expired + download-limit terminals → growth_cta_shown terminal-state once each (AC terminal)', async () => {
    for (const [code, status] of [['TRANSFER_EXPIRED', 'expired'], ['MAX_DOWNLOADS_REACHED', 'downloadLimit']] as const) {
      stubFetchProblem(410, code, 'This transfer has expired. Contact the sender for a new link.')
      const host = await renderApp(isTerminal)
      expect(useRecipientStore.getState().status).toBe(status)
      const shown = growthTelemetryLog.filter((e) => e.event === 'growth_cta_shown' && e.source === 'terminal-state')
      expect(shown).toHaveLength(1) // exactly one per page instance.
      host.remove()
      await act(async () => { useRecipientStore.getState().reset() })
      growthTelemetryLog.splice(0)
    }
  })

  it('panel CTA click → clicked post-download, upload surface renders in same tree, staging survives, no token (EC token/staging)', async () => {
    // Seed the UploadEngine so the landing surface has staged rows to preserve.
    useUploadEngine.getState().addFiles([{ name: 'a.txt', size: 10 }, { name: 'b.png', size: 5 }])
    const loc = stubLocation()
    try {
      stubDownloadFetch()

      const host = await renderApp(isActive)
      await downloadAllFiles(host) // full download → panel up.
      expect(host.querySelector('.rp-growth')).not.toBeNull()

      // Click the growth CTA — App's popstate handler swaps to the upload surface in-tree (no full reload).
      const cta = host.querySelector('.rp-growth button') as HTMLButtonElement
      await act(async () => {
        cta.click()
        for (let i = 0; i < 400 && host.querySelector('.dz-title') === null; i++) { await Promise.resolve() }
      })

      expect(host.querySelector('.dz-title')).not.toBeNull() // upload surface — the drop zone is ready.
      expect(host.querySelectorAll('.sl-row')).toHaveLength(2) // staged rows survive — no reload, no reset.
      expect(host.querySelector('.rp-growth')).toBeNull() // recipient page is gone.
      const clicked = growthTelemetryLog.filter((e) => e.event === 'growth_cta_clicked' && e.source === 'post-download')
      expect(clicked).toHaveLength(1)
      // No unlock token or recipient state transfers — pure pathname change (US-003-07 EC).
      expect(window.sessionStorage.getItem(`pd.unlock.${LINK_ID}`)).toBeNull()
      expect(useRecipientStore.getState().status).toBe('loading') // reset on unmount.
      host.remove()
    } finally {
      loc.restore()
    }
  })

  it('terminal CTA click → clicked terminal-state + upload surface renders (AC terminal → upload surface)', async () => {
    useUploadEngine.getState().addFiles([{ name: 'a.txt', size: 10 }, { name: 'b.png', size: 5 }])
    const loc = stubLocation()
    try {
      stubFetchProblem(410, 'TRANSFER_EXPIRED', 'This transfer has expired. Contact the sender for a new link.')

      const host = await renderApp(isTerminal)
      const cta = host.querySelector('.rp-terminal-action') as HTMLAnchorElement
      expect(cta).not.toBeNull()

      await act(async () => {
        cta.click()
        for (let i = 0; i < 400 && host.querySelector('.dz-title') === null; i++) { await Promise.resolve() }
      })

      expect(host.querySelector('.dz-title')).not.toBeNull() // upload surface renders.
      expect(host.querySelectorAll('.sl-row')).toHaveLength(2) // staged rows survive.
      const clicked = growthTelemetryLog.filter((e) => e.event === 'growth_cta_clicked' && e.source === 'terminal-state')
      expect(clicked).toHaveLength(1)
      host.remove()
    } finally {
      loc.restore()
    }
  })
})
