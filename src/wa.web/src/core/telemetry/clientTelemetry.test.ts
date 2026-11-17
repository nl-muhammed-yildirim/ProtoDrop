import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { clientTelemetryLog, setAiKeyForTest, setIsDevForTest, trackClientEvent } from './clientTelemetry'
import { getTraceParent, resetTraceParentForTest } from '../traceparent'

// ── T-012-02 (TA-10.2, F-TRF-012): client telemetry sink — closed list + fail-fast + correlation + transport. ──
describe('clientTelemetry (T-012-02)', () => {
  beforeEach(() => {
    resetTraceParentForTest() // fresh page load per case — the correlation id is minted once per page load.
    clientTelemetryLog.splice(0) // module-level record — keep tests hermetic (UploadEngine convention).
    setAiKeyForTest(null) // no AI key by default — M0 offline-green.
    setIsDevForTest(null) // use the real import.meta.env.DEV (true under vitest).
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 200 })))
  })

  afterEach(() => {
    setAiKeyForTest(null)
    setIsDevForTest(null)
    vi.unstubAllGlobals()
  })

  describe('closed-list fail-fast (EC closed-list)', () => {
    it('a valid name lands in clientTelemetryLog with the page-load traceparent', () => {
      trackClientEvent('upload_failed', { fileId: 'f1' })

      expect(clientTelemetryLog).toHaveLength(1)
      expect(clientTelemetryLog[0]).toEqual({ event: 'upload_failed', props: { fileId: 'f1' }, traceparent: getTraceParent() })
    })

    it('an ad-hoc name throws in dev and does NOT land in the log (fail fast — never silently logged)', () => {
      expect(() => trackClientEvent('made_up_event', { x: 1 })).toThrowError(/Unknown event name/)
      expect(clientTelemetryLog).toHaveLength(0)
    })

    it('an ad-hoc name is dropped in prod (no throw, no log entry)', () => {
      setIsDevForTest(false) // force the prod branch.
      expect(() => trackClientEvent('made_up_event', { x: 1 })).not.toThrow()
      expect(clientTelemetryLog).toHaveLength(0)
    })

    it('all four closed names are accepted without throwing', () => {
      for (const name of ['growth_cta_shown', 'growth_cta_clicked', 'upload_failed', 'download_completed']) {
        expect(() => trackClientEvent(name)).not.toThrow()
      }
      expect(clientTelemetryLog).toHaveLength(4)
    })
  })

  describe('no-AI-key offline-green (M0 convention)', () => {
    it('without VITE_WA_AI_KEY no fetch is called — console.debug only', () => {
      const debugSpy = vi.spyOn(console, 'debug').mockImplementation(() => {})
      trackClientEvent('growth_cta_shown', { source: 'post-download' })

      const fetchMock = vi.mocked(globalThis.fetch)
      expect(fetchMock).not.toHaveBeenCalled()
      expect(debugSpy).toHaveBeenCalledTimes(1)
      debugSpy.mockRestore()
    })
  })

  describe('with AI key — exactly one POST per event', () => {
    it('fires exactly one fire-and-forget POST to App Insights ingestion per event', async () => {
      setAiKeyForTest('test-key')
      const fetchMock = vi.mocked(globalThis.fetch)

      trackClientEvent('download_completed', { fileId: 'f1', bytes: 42, durationMs: 3 })
      // The POST is fire-and-forget — flush microtasks so the async send runs.
      await Promise.resolve()
      await Promise.resolve()

      expect(fetchMock).toHaveBeenCalledTimes(1)
      const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
      expect(url).toContain('dc.services.visualstudio.com/ingestion/track/events')
      expect(init.method).toBe('POST')
      expect(init.mode).toBe('no-cors') // opaque body — network failure never throws into app code.
      const headers = init.headers as Record<string, string>
      expect(headers['ikey']).toBe('test-key')
    })

    it('a network failure never throws into app code (fire-and-forget catch)', async () => {
      setAiKeyForTest('test-key')
      const fetchMock = vi.mocked(globalThis.fetch)
      fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'))

      expect(() => trackClientEvent('upload_failed', { fileId: 'f1' })).not.toThrow()
      await Promise.resolve()
      await Promise.resolve()
      // The rejection was swallowed — no unhandled rejection, no throw.
      expect(fetchMock).toHaveBeenCalledTimes(1)
    })
  })
})
