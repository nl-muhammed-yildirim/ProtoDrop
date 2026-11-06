// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'

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
