// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import App from '../../App'
import { setAdConfigForTest } from './ads/adConfig'
import { resetAdScriptsForTest } from './ads/loadAdScript'
import { useRecipientStore } from './RecipientStore'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// US-024-01 (FR-024-4, AC-024-1, AC-024-5, EC-024-2): slot placement on the recipient page.
const LINK_ID = 'ab3def5g'
const AD_CONFIG = { client: 'ca-pub-1234567890123456', unit: '1234567890' }

interface Body {
  from: string
  note: string | null
  files: Array<{ fileId: string; name: string; sizeBytes: number }>
  hasDownloadAll: boolean
  downloadsLeft: number | null
  passwordRequired: boolean
  adsEnabled?: boolean
}

const roots: Array<ReturnType<typeof createRoot>> = []

const activeBody = (overrides?: Partial<Body>): Body => ({
  from: 'Studio Nova',
  note: null,
  files: [
    { fileId: 'f1', name: 'render.mp4', sizeBytes: 2_147_483_648 },
    { fileId: 'f2', name: 'poster.png', sizeBytes: 5_242_880 },
  ],
  hasDownloadAll: true,
  downloadsLeft: 9,
  passwordRequired: false,
  ...overrides,
})

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

function stubFetch(body: Body, calls?: string[]): void {
  vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
    const url = String(input)
    calls?.push(url)
    if (url.includes('/download-url')) return json({ url: 'https://azurite.example.com/transfers/x?sig=abc', expiresAt: '2030-01-01T00:30:00Z' })
    return json(body)
  })
}

async function renderApp(settled: () => boolean): Promise<HTMLDivElement> {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  roots.push(root)
  await act(async () => {
    void root.render(createElement(App))
    for (let i = 0; i < 400 && !settled(); i++) await Promise.resolve()
  })
  return host
}

const isActive = (): boolean => useRecipientStore.getState().status === 'active'
const adScripts = () => document.head.querySelectorAll('script[data-ad-loader]')
const wait = (ms: number) => new Promise<void>((r) => setTimeout(r, ms))

beforeEach(() => {
  window.history.pushState('', '', `/t/${LINK_ID}`)
  setAdConfigForTest(AD_CONFIG)
})

afterEach(() => {
  for (const root of roots.splice(0)) act(() => { root.unmount() })
  useRecipientStore.getState().reset()
  setAdConfigForTest(undefined)
  resetAdScriptsForTest()
  window.sessionStorage.clear()
  window.history.replaceState(null, '', '/')
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('RecipientPage ad placement (US-024-01)', () => {
  it('AC-024-1: exactly one slot, below the file list and above the growth panel, no CTA/link/text before fill (FR-024-6)', async () => {
    stubFetch(activeBody({ adsEnabled: true }))
    const host = await renderApp(isActive)

    const ads = host.querySelectorAll('.rp-ad')
    expect(ads).toHaveLength(1)
    const list = host.querySelector('.rp-list') as HTMLElement
    expect(list.compareDocumentPosition(ads[0]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(ads[0].querySelectorAll('a, button')).toHaveLength(0)
    expect(ads[0].textContent).toBe('')

    await act(async () => { useRecipientStore.setState({ growthPanelVisible: true }) })
    const growth = host.querySelector('.rp-growth') as HTMLElement
    expect(growth).not.toBeNull()
    expect(ads[0].compareDocumentPosition(growth) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    host.remove()
  })

  it('AC-024-2 client half: adsEnabled false or absent -> no slot, no ad script', async () => {
    stubFetch(activeBody({ adsEnabled: false }))
    let host = await renderApp(isActive)
    expect(host.querySelectorAll('.rp-ad')).toHaveLength(0)
    expect(adScripts()).toHaveLength(0)
    host.remove()

    await act(async () => { useRecipientStore.getState().reset() })
    stubFetch(activeBody())
    host = await renderApp(isActive)
    expect(host.querySelectorAll('.rp-ad')).toHaveLength(0)
    expect(adScripts()).toHaveLength(0)
    host.remove()
  })

  it('fail closed: adsEnabled true but no ad config -> no slot, no script', async () => {
    setAdConfigForTest(null)
    stubFetch(activeBody({ adsEnabled: true }))
    const host = await renderApp(isActive)
    await act(async () => { await wait(30) })
    expect(host.querySelectorAll('.rp-ad')).toHaveLength(0)
    expect(adScripts()).toHaveLength(0)
    host.remove()
  })

  it('AC-024-5: never on the password gate; one slot after unlock', async () => {
    vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? '').toUpperCase() === 'POST') return json({ token: 'tok123' })
      const hasToken = String(input).includes('?t=tok123')
      return json(activeBody({ adsEnabled: true, passwordRequired: !hasToken }))
    })
    const isGated = (): boolean => isActive() && useRecipientStore.getState().passwordRequired === true
    const isUngated = (): boolean => isActive() && !useRecipientStore.getState().passwordRequired

    const host = await renderApp(isGated)
    expect(host.querySelectorAll('.rp-ad')).toHaveLength(0)
    expect(adScripts()).toHaveLength(0)

    const input = host.querySelector('#rp-password-input') as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'secret')
      input.dispatchEvent(new Event('input', { bubbles: true }))
      await Promise.resolve()
    })
    await act(async () => {
      ;(host.querySelector('.rp-password-card') as HTMLFormElement).requestSubmit()
      for (let i = 0; i < 400 && !isUngated(); i++) await Promise.resolve()
    })

    const ads = host.querySelectorAll('.rp-ad')
    expect(ads).toHaveLength(1)
    const list = host.querySelector('.rp-list') as HTMLElement
    expect(list.compareDocumentPosition(ads[0]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    host.remove()
  })

  it('EC-024-2: a loading slot never blocks a download (endpoint 6 still called)', async () => {
    const calls: string[] = []
    stubFetch(activeBody({ adsEnabled: true }), calls)
    const host = await renderApp(isActive)
    await act(async () => { await wait(30) }) // slot is now loading (the script never loads in jsdom)
    expect((host.querySelector('.rp-ad') as HTMLElement).dataset.state).toBe('loading')

    await act(async () => {
      ;(host.querySelector('.rp-download') as HTMLButtonElement).click()
      for (let i = 0; i < 200 && !calls.some((u) => u.includes('/download-url')); i++) await wait(0)
    })
    expect(calls.filter((u) => u.includes('/download-url'))).toHaveLength(1)
    host.remove()
  })
})
