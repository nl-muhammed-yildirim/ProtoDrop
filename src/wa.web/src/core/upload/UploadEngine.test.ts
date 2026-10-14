import { beforeEach, describe, expect, it, vi } from 'vitest'

import { BLOCK_SIZE_BYTES, MAX_ATTEMPTS, RETRY_BACKOFF_MS, fileAttempts, setSleepFnForTests, useUploadEngine, type Draft } from './UploadEngine'
import { formatBytes } from './formatBytes'

const state = () => useUploadEngine.getState()

// ── T-001-06 harness: capture every request the real BlockBlobClient makes against a stubbed fetch. ───────────────
interface CaptureEntry { method: string; url: string; bodyBytes: number | null }

let entries: CaptureEntry[] = []
function stageUrl(url: string): boolean { return url.includes('comp=block') && !url.includes('comp=blocklist') }
function commitUrl(url: string): boolean { return url.includes('comp=blocklist') }

/** blockid query param is base64 of `${fileSize}:${offset}` — decode to the offset. */
function stagedOffsetOf(url: string): number {
  const id = new URL(url).searchParams.get('blockid')
  if (id == null) throw new Error(`no blockid in stage url: ${url}`)
  return Number(atob(id).split(':')[1])
}

function stubFetch(responder?: (req: Request) => Response): void {
  entries = [] // fresh capture per test — vi.stubGlobal is NOT auto-restore, so reset explicitly here
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const req = new Request(input as string, init ?? undefined)
      let bodyBytes: number | null = null
      if (req.body != null) {
        const buf = await req.arrayBuffer()
        bodyBytes = buf.byteLength
      }
      entries.push({ method: req.method, url: req.url, bodyBytes })
      return responder ? responder(req) : new Response(null, { status: 201 })
    },
  )
}

function makeDraft(urls: string[]): Draft {
  return { draftId: 'd1', expiresInSec: 7200, files: urls.map((uploadUrl) => ({ fileId: crypto.randomUUID(), uploadUrl })) }
}

beforeEach(() => {
  state().reset()
  setSleepFnForTests(async () => {}) // FR-001-6 backoff is delay-injected — no test waits real seconds (T-001-08)
})

// ── Staging (T-001-01, TA-8.3) — unchanged. ───────────────────────────────────────────────────────────────────────
describe('UploadEngine staging (T-001-01, TA-8.3)', () => {
  it('removing one of two staged files updates the list and total immediately', () => {
    const s = state()
    s.addFiles([{ name: 'a.bin', size: 4 * 1024 * 1024 }, { name: 'b.bin', size: 8 * 1024 * 1024 }])

    const [first] = state().files
    state().remove(first.id)

    const after = state()
    expect(after.files).toHaveLength(1)
    expect(after.files[0].name).toBe('b.bin')
    expect(after.overall.totalBytes).toBe(8 * 1024 * 1024) // recomputed, not stale
    expect(after.overall.sentBytes).toBe(0)
  })

  it('keeps two files with the same name in selection order (EC-001-2)', () => {
    const s = state()
    s.addFiles([{ name: 'report.pdf', size: 100 }, { name: 'notes.txt', size: 50 }])
    s.addFiles([{ name: 'report.pdf', size: 777 }])

    const files = state().files
    expect(files.map((f) => f.name)).toEqual(['report.pdf', 'notes.txt', 'report.pdf'])
    // Order survives to finalize payload order.
    expect(files[2].size).toBe(777)
  })

  it('stages a 0-byte file without error and renders "0 B" (EC-001-3)', () => {
    state().addFiles([{ name: 'empty.bin', size: 0 }])

    const files = state().files
    expect(files).toHaveLength(1)
    expect(formatBytes(files[0].size)).toBe('0 B')
    expect(state().overall.totalBytes).toBe(0)
  })

  it('staging keeps status queued and progress 0 — no upload started', () => {
    state().addFiles([{ name: 'x.bin', size: 16 }])

    const [f] = state().files
    expect(f.status).toBe('queued')
    expect(f.progress).toBe(0)
  })

  it('resets to the empty drop-zone state (removing the last file)', () => {
    state().addFiles([{ name: 'x.bin', size: 16 }])
    const [f] = state().files
    state().remove(f.id)

    expect(state().files).toHaveLength(0)
    expect(state().overall).toEqual({ sentBytes: 0, totalBytes: 0 })
  })
})

// ── Upload loop (T-001-06, TA-8.3): real BlockBlobClient, stubbed fetch. ──────────────────────────────────────────
describe('UploadEngine upload loop (T-001-06, TA-8.3)', () => {
  it('start uploads queued files to completion via the block path', async () => {
    stubFetch()
    state().addFiles([
      { name: 'x.bin', size: 16, file: new File(['x'.repeat(16)], 'x.bin') },
      { name: 'y.bin', size: 32, file: new File(['x'.repeat(32)], 'y.bin') },
    ])

    await state().start(makeDraft([`https://host/c/x?sv=2024&sp=cwr`, `https://host/c/y?sv=2024&sp=cwr`]))

    expect(state().files.every((f) => f.status === 'done')).toBe(true)
    expect(state().overall).toEqual({ sentBytes: 48, totalBytes: 48 })
    // Each file went through exactly one stage PUT + one commit — the block path is the only path (FR-001-7).
    for (const mark of ['/x?', '/y?']) {
      expect(entries.filter((e) => e.url.includes(mark) && stageUrl(e.url))).toHaveLength(1)
      expect(entries.filter((e) => e.url.includes(mark) && commitUrl(e.url))).toHaveLength(1)
    }
  })

  it('retry resumes a failed file — progress is preserved, not reset (FR-001-5)', async () => {
    const size = BLOCK_SIZE_BYTES * 2 // two blocks; the second block's stage dies while the network is down
    let killSecondBlock = true
    stubFetch((req) => (killSecondBlock && stageUrl(req.url) && stagedOffsetOf(req.url) !== 0 ? new Response('gone', { status: 404 }) : new Response(null, { status: 201 })))
    const [id] = state().addFiles([{ name: 'big.bin', size, file: new File(['x'.repeat(size)], 'big.bin') }])

    await state().start(makeDraft([`https://host/c/big?sv=2024&sp=cwr`]))

    // First block completed before the second stage was killed — progress frozen at those bytes, NOT reset to 0.
    expect(state().files[0].status).toBe('failed')
    expect(state().files[0].progress).toBe(BLOCK_SIZE_BYTES / size)
    expect(state().overall.sentBytes).toBe(BLOCK_SIZE_BYTES)

    const runLength = entries.length
    killSecondBlock = false // retry: network back, all stages succeed
    await state().retry(id)

    expect(state().files[0].status).toBe('done')
    expect(state().files[0].progress).toBe(1)
    expect(state().overall.sentBytes).toBe(size)
    // Only the unfinished block is re-sent — finished bytes are not re-uploaded.
    const replay = entries.slice(runLength).filter((e) => e.method === 'PUT' && stageUrl(e.url))
    expect(replay.map((e) => stagedOffsetOf(e.url)).sort((a, b) => a - b)).toEqual([BLOCK_SIZE_BYTES])
  })
})

// ── Exit checks (T-001-06 task doc). ──────────────────────────────────────────────────────────────────────────────
describe('UploadEngine exit checks (T-001-06)', () => {
  it('a 25 MB file uploads via multiple 8 MiB blocks — never one giant PUT', async () => {
    const size = 25 * 1024 * 1024 // > CHUNK_THRESHOLD_BYTES → block path required (FR-001-7)
    stubFetch()
    state().addFiles([{ name: 'big.bin', size, file: new File(['x'.repeat(size)], 'big.bin') }])

    await state().start(makeDraft([`https://host/c/big?sv=2024&sp=cwr`]))

    const stages = entries.filter((e) => e.method === 'PUT' && stageUrl(e.url))
    expect(stages).toHaveLength(4) // 8 MiB ×3 + 1 MiB — exactly the block plan for 25 MB
    expect([...stages.map((e) => stagedOffsetOf(e.url))].sort((a, b) => a - b)).toEqual([0, BLOCK_SIZE_BYTES, BLOCK_SIZE_BYTES * 2, BLOCK_SIZE_BYTES * 3])
    expect(Math.max(...stages.map((e) => e.bodyBytes ?? 0))).toBe(BLOCK_SIZE_BYTES) // no request carries more than one block
    expect(entries.filter((e) => commitUrl(e.url))).toHaveLength(1) // single commit with the full block list
    expect(state().files[0].status).toBe('done')
    expect(state().overall.sentBytes).toBe(size)
  })

  it('files upload strictly one at a time — A is done before B starts', async () => {
    const size = BLOCK_SIZE_BYTES + 5 // two blocks each; real block sizes so both files use the pool path
    let snapshotAtBStart: Array<'queued' | 'uploading' | 'done' | 'failed'> = []
    stubFetch((req) => {
      if (snapshotAtBStart.length === 0 && req.url.includes('/b?')) snapshotAtBStart = state().files.map((f) => f.status)
      return new Response(null, { status: 201 })
    })
    state().addFiles([
      { name: 'a.bin', size, file: new File(['x'.repeat(size)], 'a.bin') },
      { name: 'b.bin', size, file: new File(['x'.repeat(size)], 'b.bin') },
    ])

    await state().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`, `https://host/c/b?sv=2024&sp=cwr`]))

    const lastA = entries.reduce((acc, e) => Math.max(acc, e.url.includes('/a?') ? entries.indexOf(e) : acc), -1)
    const firstB = entries.findIndex((e) => e.url.includes('/b?'))
    expect(lastA).toBeGreaterThanOrEqual(0) // A actually uploaded (stages + commit captured)
    expect(firstB).toBeGreaterThan(lastA) // every one of A's requests, including its commit, precedes B's first request
    // At the moment B's first block hits the network: A is done and B has just started.
    expect(snapshotAtBStart).toEqual(['done', 'uploading'])
  })

  it('killing the network mid-file then re-running resumes — finished bytes are not re-uploaded (FR-001-5)', async () => {
    const size = BLOCK_SIZE_BYTES * 3 // three blocks; only offset 0 survives run 1 (network dies mid-file)
    let killNonZeroBlocks = true
    stubFetch((req) => (killNonZeroBlocks && stageUrl(req.url) && stagedOffsetOf(req.url) !== 0 ? new Response('timeout', { status: 408 }) : new Response(null, { status: 201 })))
    const [id] = state().addFiles([{ name: 'big.bin', size, file: new File(['x'.repeat(size)], 'big.bin') }])

    await state().start(makeDraft([`https://host/c/big?sv=2024&sp=cwr`]))

    expect(state().files[0].status).toBe('failed')
    // Progress frozen at last completed bytes — neither 0 nor 1.
    expect(state().files[0].progress).toBe(BLOCK_SIZE_BYTES / size)
    const runLength = entries.length
    killNonZeroBlocks = false // network back — the retry re-runs only what had not completed
    await state().retry(id)

    expect(state().files[0].status).toBe('done')
    expect(state().overall.sentBytes).toBe(size)
    // Only the blocks that had NOT completed in the killed run are staged again — finished bytes never re-uploaded.
    const replay = entries.slice(runLength).filter((e) => e.method === 'PUT' && stageUrl(e.url))
    expect(replay.map((e) => stagedOffsetOf(e.url)).sort((a, b) => a - b)).toEqual([BLOCK_SIZE_BYTES, BLOCK_SIZE_BYTES * 2])
  })
})

// ── Per-block retry + backoff (T-001-08, FR-001-6): real BlockBlobClient, delay-injected. ────────────────────────
describe('UploadEngine retry/backoff (T-001-08)', () => {
  it('a block failing 5 times marks the row failed with Retry; sibling files continue unaffected (AC-001-3)', async () => {
    const size = 16 // one block per file — smallest real path through the pool
    stubFetch((req) => (stageUrl(req.url) && req.url.includes('/a?') ? new Response('gone', { status: 404 }) : new Response(null, { status: 201 })))
    const [idA] = state().addFiles([
      { name: 'a.bin', size, file: new File(['x'.repeat(size)], 'a.bin') },
      { name: 'b.bin', size, file: new File(['y'.repeat(size)], 'b.bin') },
    ])

    await state().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`, `https://host/c/b?sv=2024&sp=cwr`]))

    const aStages = entries.filter((e) => stageUrl(e.url) && e.url.includes('/a?'))
    expect(aStages).toHaveLength(MAX_ATTEMPTS) // exactly 5 requests for the one block — deterministic IDs keep re-issuing idempotent (FR-001-6)
    const record = fileAttempts.get(idA)
    expect(record?.retries).toBe(MAX_ATTEMPTS - 1) // FR-001-6 counter — T-001-10's upload_failed payload reads this.
    expect(record?.lastErrorCode ?? '').not.toBe('') // TA-10.2 last error code recorded for the same emission.
    expect(state().files[0].status).toBe('failed') // row-local failure — no global state (FR-001-6)
    expect(state().files[1].status).toBe('done') // sibling B ran to completion after A's run settled (AC-001-3)
  })

  it('backoff waits the exact FR-001-6 schedule between attempts — no wait before attempt 1 or after the last', async () => {
    const waits: number[] = []
    setSleepFnForTests(async (ms) => { waits.push(ms) }) // capture instead of sleeping — real ms would blow the 5 s timeout.
    stubFetch(() => new Response('timeout', { status: 408 }))
    state().addFiles([{ name: 'a.bin', size: 16, file: new File(['x'.repeat(16)], 'a.bin') }])

    await state().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`]))

    expect(waits).toEqual([...RETRY_BACKOFF_MS]) // 0.5 s, 1 s, 2 s, 4 s (capped) — exact order, MAX_ATTEMPTS-1 waits for MAX_ATTEMPTS requests (TA-8.3)
  })

  it('retry after a partial failure re-enters skipping finished bytes — no finished block re-uploaded (FR-001-5/6)', async () => {
    const size = BLOCK_SIZE_BYTES * 2 // two blocks; the second block's stage dies while the network is down.
    let killSecondBlock = true
    stubFetch((req) => (killSecondBlock && stageUrl(req.url) && stagedOffsetOf(req.url) !== 0 ? new Response('gone', { status: 404 }) : new Response(null, { status: 201 })))
    const [id] = state().addFiles([{ name: 'big.bin', size, file: new File(['x'.repeat(size)], 'big.bin') }])

    await state().start(makeDraft([`https://host/c/big?sv=2024&sp=cwr`]))
    expect(state().files[0].status).toBe('failed')
    expect(fileAttempts.get(id)?.retries).toBe(MAX_ATTEMPTS - 1) // the first run burned its full FR-001-6 budget...

    const runLength = entries.length
    killSecondBlock = false // network back — retry() re-arms the counter and resumes from the last completed block.
    await state().retry(id)

    expect(state().files[0].status).toBe('done')
    expect(fileAttempts.get(id)?.retries).toBe(0) // fresh run recorded no new retries (T-001-10 counts per failure run, not cumulative).
    const replay = entries.slice(runLength).filter((e) => e.method === 'PUT' && stageUrl(e.url))
    expect(replay.map((e) => stagedOffsetOf(e.url)).sort((a, b) => a - b)).toEqual([BLOCK_SIZE_BYTES]) // only the unfinished offset re-sent.
  })

  it('the row-local state machine reaches every FR status — queued → uploading → done/failed (T-001-08 exit check)', async () => {
    const size = 16
    stubFetch((req) => (stageUrl(req.url) && req.url.includes('/b?') ? new Response('gone', { status: 404 }) : new Response(null, { status: 201 })))
    const [idA, idB] = state().addFiles([
      { name: 'a.bin', size, file: new File(['x'.repeat(size)], 'a.bin') },
      { name: 'b.bin', size, file: new File(['y'.repeat(size)], 'b.bin') },
    ])
    const seenA: string[] = ['queued'] // queued is the staged status (T-001-01) — predates subscribing.
    const seenB: string[] = ['queued']
    let unsub = () => {}
    unsub = useUploadEngine.subscribe((s) => { for (const f of s.files) if (f.id === idA || f.id === idB) (f.id === idA ? seenA : seenB).push(f.status) })

    await state().start(makeDraft([`https://host/c/a?sv=2024&sp=cwr`, `https://host/c/b?sv=2024&sp=cwr`]))
    unsub()

    expect(seenA).toContain('uploading') // queued → uploading happened live (serialization: B only starts after A settles).
    expect(seenA[seenA.length - 1]!).toBe('done') // A's final state.
    expect(seenB).toContain('uploading')
    expect(seenB[seenB.length - 1]!).toBe('failed') // B's final state — row-local, no shared failure flag (FR-001-6).
  })
})

describe('formatBytes (TA-8.5, 1024-based)', () => {
  it('formats bytes in the right unit', () => {
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(1024)).toBe('1.00 KB')
    expect(formatBytes(8 * 1024 * 1024)).toBe('8.00 MB')
    expect(formatBytes(5 * 1024 ** 3)).toBe('5.00 GB')
  })
})
