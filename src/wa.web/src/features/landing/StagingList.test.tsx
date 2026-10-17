// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import StagingList from './StagingList'
import { useUploadEngine, setSleepFnForTests, type Draft } from '../../core/upload/UploadEngine'
import { formatBytes } from '../../core/upload/formatBytes'
import { resetPlanLimits, setPlanLimits } from '../../core/upload/planLimits'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

// Client mount (not renderToStaticMarkup): the store snapshot is read on first
// commit; SSR would capture the pre-staging state.
function renderClient(el: React.ReactElement): string {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(el)
  })
  const html = host.innerHTML
  host.remove()
  return html
}

function stage(name: string, size = 0): string[] {
  return useUploadEngine.getState().addFiles([{ name, size }])
}
// ── T-001-07 harness (local copy of the stub-fetch pattern — no cross-file coupling). ───────────────────────────
 function stubFetch(responder?: (req: Request) => Response | Promise<Response>): void {
   vi.stubGlobal(
     'fetch',
     async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
       // Re-wrap without the SDK's AbortSignal — jsdom realm mismatch: their signal fails `instanceof` against this realm's AbortSignal.
       const req = new Request(typeof input === 'string' ? input : String(input), { method: init?.method ?? 'GET' })
       return responder ? await responder(req) : new Response(null, { status: 201 })
     },
   )
 }

function makeDraft(urls: string[]): Draft {
  return { draftId: 'd1', expiresInSec: 7200, files: urls.map((uploadUrl) => ({ fileId: crypto.randomUUID(), uploadUrl })) }
}

beforeEach(() => {
  useUploadEngine.getState().reset()
  resetPlanLimits() // back to the Free guest defaults between tests.
})

describe('StagingList', () => {
  it('renders one row per staged file; duplicate names both render (EC-001-2)', () => {
    stage('report.pdf', 3)
    stage('report.pdf', 7)
    const html = renderClient(createElement(StagingList))
    expect(html.match(/class="sl-row"/g)).toHaveLength(2)
    // Both rows carry the same name with a title attr for truncation.
    expect(html.match(/title="report\.pdf">report\.pdf/g)).toHaveLength(2)
  })

  it('removing one of two files updates the list and total immediately; Send. stays enabled (AC-001-3)', () => {
    const [idA] = stage('a.bin', 3)
    const [idB] = stage('b.txt', 7)

    expect(renderClient(createElement(StagingList))).toContain('>2 files · 10 B<')

    useUploadEngine.getState().remove(idA) // the ✕ button's onClick is exactly this call.

    const after = renderClient(createElement(StagingList))
    expect(after.match(/class="sl-row"/g)).toHaveLength(1)
    expect(after).toContain('>b.txt</span>')
    expect(after).toContain('>7 B<')
    expect(after).toContain('>1 file · 7 B<')
    // Send. is enabled: no disabled attr on the send button.
    const send = after.match(/class="btn-primary sl-send"[^>]*>/)![0]
    expect(send).not.toContain('disabled')

    useUploadEngine.getState().remove(idB)
  })

  it('a 0-byte row shows "0 B" (EC-001-3)', () => {
    stage('empty.dat', 0)
    const html = renderClient(createElement(StagingList))
    expect(html).toContain('>empty.dat</span>')
    expect(html).toContain('>0 B<') // row size
    expect(html).toContain('>1 file · 0 B<')
  })

  it('Send. is disabled while the list is empty', () => {
    const html = renderClient(createElement(StagingList))
    expect(html).toContain('>0 files · 0 B<')
    const send = html.match(/class="btn-primary sl-send"[^>]*>/)![0]
    expect(send).toContain('disabled=""')
  })

  describe('T-001-04 limit pre-check', () => {
    it('total over MAX_TRANSFER_SIZE: message names the exact limit, Send. disabled (AC-001 shape)', () => {
      const threeGB = 3 * 1024 ** 3 // real staged sizes; the check is pure arithmetic.
      stage('a.bin', threeGB)
      stage('b.bin', threeGB) // total 6.00 GB vs a 5.00 GB limit.

      const html = renderClient(createElement(StagingList))
      expect(html).toContain('>Transfer size 6.00 GB exceeds the 5.00 GB limit. Remove files to continue.<')
      // formatBytes renders "X.00 GB" — assert on its output, not a hand-typed value.
      expect(html).toContain(`exceeds the ${formatBytes(5368709120)} limit`)
      const send = html.match(/class="btn-primary sl-send"[^>]*>/)![0]
      expect(send).toContain('disabled=""') // disabled button: opacity 0.55 + tooltip via wrapper span.
      // No per-file marking: both rows are individually under the per-file limit.
      expect(html.match(/sl-row-over/g)).toBeNull()
    })

    it('a single oversized row is marked and the per-file limit is named (exit check 2)', () => {
      setPlanLimits({ maxTransferSize: 5368709120, maxSingleFile: 4 * 1024 ** 3 }) // a stricter per-file cap.
      stage('big.bin', 5 * 1024 ** 3) // over the per-file limit only (total still in range).

      const html = renderClient(createElement(StagingList))
      expect(html).toContain('>This file exceeds the 4.00 GB per-file limit. Remove it to continue.<')
      // The row is exactly the one staged <li>, marked.
      const rows = html.match(/<li class="sl-row[^"]*"/g)!
      expect(rows).toHaveLength(1)
      expect(rows[0]).toContain('sl-row-over')
    })

    it('removing files until the list fits clears the message and re-enables Send. (exit check 3)', () => {
      const threeGB = 3 * 1024 ** 3
      stage('a.bin', threeGB)
      const [idB] = stage('b.bin', threeGB) // over the limit with both staged.

      expect(renderClient(createElement(StagingList))).toContain('>Transfer size')

      useUploadEngine.getState().remove(idB) // the ✕ button's onClick is exactly this call.

      const after = renderClient(createElement(StagingList))
      expect(after).not.toContain('sl-limit-msg') // message cleared.
      const send = after.match(/class="btn-primary sl-send"[^>]*>/)![0]
      expect(send).not.toContain('disabled=""') // Send. re-enabled; list non-empty and in range.
    })
  })
  describe('T-001-07 progress (FR-001-5)', () => {
    const tick = (ms = 0) => new Promise<void>((r) => setTimeout(r, ms))
    // Poll until A's status is 'done' — B is guaranteed in flight by then (A completes only after B's stage fetch issued).
    async function waitUntilDone(id: string): Promise<void> {
      for (let i = 0; i < 500 && useUploadEngine.getState().files.find((f) => f.id === id)?.status !== 'done'; i++) await tick(10)
    }

    it('rows show a live byte-based percentage and the overall line shows combined progress (AC-001-1)', async () => {
      stubFetch()
      const [idA] = useUploadEngine.getState().addFiles([
        { name: 'a.bin', size: 48, file: new File(['x'.repeat(48)], 'a.bin') },
        { name: 'b.bin', size: 48, file: new File(['y'.repeat(48)], 'b.bin') },
      ])
      void useUploadEngine.getState().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`, `https://host/c/b?sv=2024&sp=cwr`]))
      await waitUntilDone(idA) // capture while B is still uploading.

      const overall = useUploadEngine.getState().overall // read synchronously — no await between render and this line, so the bytes cannot move.
      const html = renderClient(createElement(StagingList))
      const rowB = html.split('<li class="sl-row')[1]! // A is done, B in flight — its percent reflects live bytes so far.
      expect(rowB).toMatch(/class="sl-percent">\d+%/)
      expect(rowB).toContain('aria-hidden="true">') // the 3 px track is hidden from AT — the percent carries the value.
      expect(html).toContain(`Uploading… ${formatBytes(overall.sentBytes)} of ${formatBytes(overall.totalBytes)}`)
    })

    it('a completed row shows the done check while the next row begins filling (exit check 2)', async () => {
      stubFetch((req) => (req.url.includes('/b?') ? new Promise<Response>(() => {}) : new Response(null, { status: 201 }))) // B's fetch never settles.
      const [idA] = useUploadEngine.getState().addFiles([
        { name: 'a.bin', size: 48, file: new File(['x'.repeat(48)], 'a.bin') },
        { name: 'b.bin', size: 48, file: new File(['y'.repeat(48)], 'b.bin') },
      ])
      void useUploadEngine.getState().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`, `https://host/c/b?sv=2024&sp=cwr`]))
      await waitUntilDone(idA) // A done, B in flight — the engine loop is mid-run.

      const html = renderClient(createElement(StagingList))
      const rows = html.split('<li class="sl-row').slice(1)
      expect(rows[0]!).toContain('class="sl-done"') // A: done check.
      expect(rows[0]!).not.toContain('aria-label="Remove b.bin"') // row 1 is really a's row, not the other one.
      expect(rows[1]!).toContain('class="sl-progress-track"') // B begins filling — no polling: this came from store transitions alone.
      expect(rows[1]!).not.toContain('class="sl-done"')
    })

    it('the polite status region changes only at block boundaries — no per-byte spam (F-TRF-016-3)', async () => {
      const seen = new Set<string>() // distinct overall byte pairs across the whole run.
      const unsub = useUploadEngine.subscribe((s) => seen.add(`${s.overall.sentBytes}:${s.overall.totalBytes}`))
      const n = 4
      stubFetch()
      const entries: { name: string; size: number; file: File }[] = []
      for (let i = 0; i < n; i++) entries.push({ name: `f${i}.bin`, size: 48, file: new File(['x'.repeat(48)], `f${i}.bin`) })
      useUploadEngine.getState().addFiles(entries)
      const urls = Array.from({ length: n }, (_, i) => `https://host/c/f${i}?sv=2024&sp=cwr`)
      await useUploadEngine.getState().start(makeDraft(urls))
      unsub()

      expect(seen.size).toBeLessThanOrEqual(n + 1) // one new byte value per file completion — never per byte.
    })
  })
  describe('T-001-08 row-local failure + Retry (FR-001-6)', () => {
    const killBlock = (req: Request, mark: string) => req.url.includes('comp=block') && !req.url.includes('comp=blocklist') && req.url.includes(mark) // the local stub keeps no capture — filter by URL shape.

    it('a failed row turns --danger with "Upload failed — retry" and a Retry button; sibling rows are unaffected (AC-001-3)', async () => {
      setSleepFnForTests(async () => {}) // backoff delay-injected here too — the real 7.5 s schedule would blow the timeout.
      stubFetch((req) => (killBlock(req, '/a?') ? new Response('gone', { status: 404 }) : new Response(null, { status: 201 }))) // A's block dies 5 times; B is untouched.
      const [idA] = useUploadEngine.getState().addFiles([
        { name: 'a.bin', size: 48, file: new File(['x'.repeat(48)], 'a.bin') },
        { name: 'b.bin', size: 48, file: new File(['y'.repeat(48)], 'b.bin') },
      ])
      await useUploadEngine.getState().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`, `https://host/c/b?sv=2024&sp=cwr`])) // start settles: A failed, B done.

      const rows = renderClient(createElement(StagingList)).split('<li class="sl-row')
      expect(rows[1]!).toContain('sl-row-failed"') // the failure marker is on the row's <li> only while status === 'failed'.
      expect(rows[1]!).toContain('>Upload failed — retry<') // the FR-001-6 text (rendered in --danger via .sl-failed-msg).
      expect(rows[1]!).toContain('class="sl-retry"')
      expect(rows[1]!).toContain('aria-label="Retry a.bin"')
      void idA // the row identity is proven by its aria-label and the sibling checks below.
      expect(rows[2]!).not.toContain('sl-row-failed') // B: unaffected — row-local failure (FR-001-6).
      expect(rows[2]!).toContain('class="sl-done"')
    })

    it('pressing Retry re-enters the engine — the danger text and button leave once the file is done', async () => {
      setSleepFnForTests(async () => {})
      let kill = true
      stubFetch((req) => (kill && killBlock(req, '/a?') ? new Response('gone', { status: 404 }) : new Response(null, { status: 201 })))
      const [idA] = useUploadEngine.getState().addFiles([{ name: 'a.bin', size: 48, file: new File(['x'.repeat(48)], 'a.bin') }])
      await useUploadEngine.getState().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`]))
      expect(renderClient(createElement(StagingList))).toContain('class="sl-failed-msg"') // failed row before the retry.

      kill = false // network back — the Retry button's onClick is exactly this call (FR-001-5 resume).
      await useUploadEngine.getState().retry(idA)

      const after = renderClient(createElement(StagingList))
      expect(after).not.toContain('sl-failed-msg') // danger text gone — the row left the failed state.
      expect(after).not.toContain('class="sl-retry"')
      expect(after).toContain('>100%<') // progress restored to full via resume, not reset.
    })
  })
})
