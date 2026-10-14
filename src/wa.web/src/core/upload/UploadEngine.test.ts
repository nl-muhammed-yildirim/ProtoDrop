import { beforeEach, describe, expect, it } from 'vitest'

import { useUploadEngine } from './UploadEngine'
import { formatBytes } from './formatBytes'

const state = () => useUploadEngine.getState()

beforeEach(() => {
  useUploadEngine.getState().reset()
})

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

  it('start flips queued files to uploading (stub — loop lands in T-001-06)', () => {
    state().addFiles([{ name: 'x.bin', size: 16 }, { name: 'y.bin', size: 32 }])
    state().start({ draftId: 'd1', expiresInSec: 7200, files: [] })

    expect(state().files.every((f) => f.status === 'uploading')).toBe(true)
  })

  it('retry flips a failed file back to uploading with progress reset (stub)', () => {
    state().addFiles([{ name: 'x.bin', size: 16 }])
    const [f] = state().files
    useUploadEngine.setState({ files: [{ ...f, status: 'failed', progress: 0.5 }] })

    state().retry(f.id)

    const after = state().files[0]
    expect(after.status).toBe('uploading')
    expect(after.progress).toBe(0)
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
