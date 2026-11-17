// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { clientTelemetryLog } from '../../core/telemetry/clientTelemetry'
import { useRecipientStore } from './RecipientStore'

// ── T-012-02 (TA-10.2): download_completed telemetry emission. ──
describe('RecipientStore download_completed (T-012-02)', () => {
  beforeEach(() => {
    clientTelemetryLog.splice(0) // module-level record — keep tests hermetic.
    useRecipientStore.getState().reset()
  })

  it('completed download emits download_completed once with bytes and durationMs >= 0', async () => {
    // Seed the store with a file so the telemetry can read its sizeBytes.
    useRecipientStore.setState({
      status: 'active',
      files: [{ fileId: 'f1', name: 'test.bin', sizeBytes: 1024 }],
      downloadedFileIds: [],
      downloadsLeft: 5,
      growthPanelVisible: false,
    })

    // Stub the download URL endpoint to return a minted SAS URL (same pattern as RecipientPage.test.tsx).
    const mintedUrl = 'https://azurite.example.com/transfers/fake-sas?sig=abc'
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/download-url')) {
          return new Response(JSON.stringify({ url: mintedUrl, expiresAt: '2030-01-01T00:30:00Z' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })
      },
    )

    // jsdom can't navigate — capture window.location.href via a fake getter (same idiom as RecipientPage.test.tsx).
    const realLocation = window.location as unknown
    const fakeLocation: Record<string, unknown> = {}
    let assignedHref: string | null = null
    Object.defineProperty(fakeLocation, 'href', {
      configurable: true,
      get: () => '',
      set: (v: string) => { assignedHref = v },
    })
    const real = window.location as unknown as Location
    Object.defineProperty(fakeLocation, 'pathname', { configurable: true, get: () => real.pathname })
    Object.defineProperty(window, 'location', { configurable: true, get: () => fakeLocation })

    try {
      useRecipientStore.getState().download('link1', 'f1')

      // Wait for the async IIFE to complete (endpoint 6 fetch + browserDownloadUrl + navigation).
      // crypto.subtle.importKey/sign resolve on a real async boundary — microtask flushes don't advance them, so tick via setTimeout.
      for (let i = 0; i < 400 && assignedHref === null; i++) { await new Promise((r) => setTimeout(r, 0)) }

      expect(assignedHref).not.toBeNull() // navigation happened — download completed.

      const downloadCompleted = clientTelemetryLog.filter((e) => e.event === 'download_completed')
      expect(downloadCompleted).toHaveLength(1) // exactly one emission.
      expect(downloadCompleted[0].props.fileId).toBe('f1')
      expect(downloadCompleted[0].props.bytes).toBe(1024) // bytes = f.sizeBytes from RecipientFile state.
      expect(downloadCompleted[0].props.durationMs).toBeGreaterThanOrEqual(0) // durationMs is client-measured.
    } finally {
      Object.defineProperty(window, 'location', { configurable: true, value: realLocation })
      vi.unstubAllGlobals()
    }
  })
})

// ── T-004-03 (FR-004-6, EC-004-4): downloadAll — endpoint 7 polling flow. ──
describe('RecipientStore downloadAll (T-004-03)', () => {
  const SAS = 'https://azurite.example.com/transfers/zip-sas?sig=abc'
  const FILES = [
    { fileId: 'f1', name: 'a.bin', sizeBytes: 1000 },
    { fileId: 'f2', name: 'b.bin', sizeBytes: 500 },
  ]
  const json = (status: number, body: unknown) =>
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

  let assignedHref: string | null = null
  let realLocation: unknown

  function stubLocation(): void {
    realLocation = window.location as unknown
    const fakeLocation: Record<string, unknown> = {}
    Object.defineProperty(fakeLocation, 'href', { configurable: true, get: () => '', set: (v: string) => { assignedHref = v } })
    const real = window.location as unknown as Location
    Object.defineProperty(fakeLocation, 'pathname', { configurable: true, get: () => real.pathname })
    Object.defineProperty(window, 'location', { configurable: true, get: () => fakeLocation })
  }

  /** Real macrotask ticks - crypto.subtle (browserDownloadUrl) does not advance on microtask flushes. */
  async function waitFor(cond: () => boolean): Promise<void> {
    for (let i = 0; i < 600 && !cond(); i++) { await new Promise((r) => setTimeout(r, 0)) }
  }

  function seed(downloadsLeft: number | null = 5): void {
    useRecipientStore.setState({ status: 'active', files: FILES, downloadedFileIds: [], downloadsLeft, growthPanelVisible: false })
  }

  /** Stubs endpoint 7 with a scripted response queue (last entry repeats); returns the recorded call URLs. */
  function stubEndpoint7(script: Array<() => Response>): string[] {
    const calls: string[] = []
    let n = 0
    vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/download-all-url')) {
        calls.push(url)
        return script[Math.min(n++, script.length - 1)]()
      }
      if (url.includes('/download-url')) return json(200, { url: SAS, expiresAt: null })
      return json(200, {})
    })
    return calls
  }

  beforeEach(() => {
    assignedHref = null
    clientTelemetryLog.splice(0)
    useRecipientStore.getState().reset()
    window.sessionStorage.clear()
    stubLocation()
  })

  afterEach(() => {
    vi.useRealTimers()
    useRecipientStore.getState().reset()
    Object.defineProperty(window, 'location', { configurable: true, value: realLocation })
    vi.unstubAllGlobals()
  })

  it('cached zip: one endpoint-7 call, never preparing, download_completed once with fileId null (US-004-02)', async () => {
    seed()
    const calls = stubEndpoint7([() => json(200, { url: SAS })])
    const phases: string[] = []
    const unsub = useRecipientStore.subscribe((s) => { phases.push(s.downloadAllPhase) })

    useRecipientStore.getState().downloadAll('link1')
    await waitFor(() => assignedHref !== null && useRecipientStore.getState().downloadAllPhase === 'idle')
    unsub()

    expect(calls).toHaveLength(1)
    expect(assignedHref).not.toBeNull()
    expect(phases).not.toContain('preparing')
    expect(useRecipientStore.getState().downloadAllPhase).toBe('idle')
    const completed = clientTelemetryLog.filter((e) => e.event === 'download_completed')
    expect(completed).toHaveLength(1)
    expect(completed[0].props.fileId).toBeNull()
    expect(completed[0].props.bytes).toBe(1500)
  })

  it('fresh zip: 202, 202, 200 → preparing then navigation; every file completed, downloadsLeft - 1 (FR-004-6)', async () => {
    seed(5)
    const calls = stubEndpoint7([
      () => json(202, { pollAfterSec: 0.01 }),
      () => json(202, { pollAfterSec: 0.01 }),
      () => json(200, { url: SAS }),
    ])
    const phases: string[] = []
    const unsub = useRecipientStore.subscribe((s) => { phases.push(s.downloadAllPhase) })

    useRecipientStore.getState().downloadAll('link1')
    await waitFor(() => assignedHref !== null && useRecipientStore.getState().downloadAllPhase === 'idle')
    unsub()

    expect(phases).toContain('preparing')
    expect(calls).toHaveLength(3)
    expect(assignedHref).not.toBeNull()
    const s = useRecipientStore.getState()
    expect(s.downloadedFileIds).toEqual(['f1', 'f2'])
    expect(s.growthPanelVisible).toBe(true)
    expect(s.downloadsLeft).toBe(4)
    expect(s.downloadAllFailed).toBe(false)
  })

  it('3-minute ceiling: still preparing at 178 s, then idle + downloadAllFailed, no navigation (US-004-01 alt flow)', async () => {
    seed()
    const calls = stubEndpoint7([() => json(202, { pollAfterSec: 2 })])
    vi.useFakeTimers()

    useRecipientStore.getState().downloadAll('link1')
    await vi.advanceTimersByTimeAsync(178_000)
    expect(useRecipientStore.getState().downloadAllPhase).toBe('preparing')
    expect(useRecipientStore.getState().downloadAllFailed).toBe(false)

    await vi.advanceTimersByTimeAsync(4_000)
    expect(useRecipientStore.getState().downloadAllPhase).toBe('idle')
    expect(useRecipientStore.getState().downloadAllFailed).toBe(true)
    expect(assignedHref).toBeNull()
    const callsAtFail = calls.length
    await vi.advanceTimersByTimeAsync(10_000)
    expect(calls.length).toBe(callsAtFail) // polling stopped
  })

  it('terminal MAX_DOWNLOADS_REACHED → downloadLimit screen state', async () => {
    seed()
    stubEndpoint7([() => json(410, { code: 'MAX_DOWNLOADS_REACHED', details: 'All downloads have been used.' })])

    useRecipientStore.getState().downloadAll('link1')
    await waitFor(() => useRecipientStore.getState().status === 'downloadLimit')

    expect(useRecipientStore.getState().status).toBe('downloadLimit')
    expect(useRecipientStore.getState().terminalMessage).toBe('All downloads have been used.')
  })

  it('503 INTERNAL → idle + downloadAllFailed; per-file download still works afterwards (EC-004-4)', async () => {
    seed()
    stubEndpoint7([() => json(503, { code: 'INTERNAL', details: 'x' })])

    useRecipientStore.getState().downloadAll('link1')
    await waitFor(() => useRecipientStore.getState().downloadAllFailed)

    expect(useRecipientStore.getState().downloadAllPhase).toBe('idle')
    expect(useRecipientStore.getState().downloadAllFailed).toBe(true)

    useRecipientStore.getState().download('link1', 'f1')
    await waitFor(() => assignedHref !== null && useRecipientStore.getState().downloadingFileId === null)
    expect(assignedHref).not.toBeNull()
    expect(useRecipientStore.getState().downloadedFileIds).toEqual(['f1'])
  })

  it('reset() during preparing cancels polling - no further fetch, no state write', async () => {
    seed()
    const calls = stubEndpoint7([() => json(202, { pollAfterSec: 2 })])
    vi.useFakeTimers()

    useRecipientStore.getState().downloadAll('link1')
    await vi.advanceTimersByTimeAsync(0)
    expect(useRecipientStore.getState().downloadAllPhase).toBe('preparing')
    const before = calls.length

    useRecipientStore.getState().reset()
    await vi.advanceTimersByTimeAsync(30_000)

    expect(calls.length).toBe(before)
    expect(useRecipientStore.getState().downloadAllPhase).toBe('idle')
    expect(useRecipientStore.getState().downloadAllFailed).toBe(false)
  })

  it('per-file download clears downloadingFileId even with unlimited downloads (downloadsLeft === null)', async () => {
    seed(null)
    stubEndpoint7([() => json(200, { url: SAS })])

    useRecipientStore.getState().download('link1', 'f1')
    await waitFor(() => assignedHref !== null && useRecipientStore.getState().downloadingFileId === null)

    expect(useRecipientStore.getState().downloadingFileId).toBeNull()
    expect(useRecipientStore.getState().downloadsLeft).toBeNull()
    expect(useRecipientStore.getState().downloadedFileIds).toEqual(['f1'])
  })
})
