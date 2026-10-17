import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import DropZone, { collectFiles, pastedImages } from './DropZone'
function file(name: string, size = 0): File { return new File(['a'.repeat(size)], name, { type: 'application/octet-stream', lastModified: 1 }) }

describe('collectFiles', () => {
  it('stages every dropped file with correct names and sizes; no skips', () => {
    const a = file('a.bin', 3)
    const b = file('b.txt', 7)
    const result = collectFiles({ files: [a, b], entries: [0, 1] })
    expect(result.entries).toEqual([
      { name: 'a.bin', size: 3, file: a },
      { name: 'b.txt', size: 7, file: b },
    ])
    expect(result.skipped).toBe(0)
  })

  it('reports an empty folder drop as skipped (AC-001-4 signal)', () => {
    const result = collectFiles({ files: [], entries: [0] })
    expect(result.entries).toEqual([])
    expect(result.skipped).toBe(1)
  })

  it('plain file drops have no entries → zero skipped', () => {
    const c = file('c.dat', 42)
    const result = collectFiles({ files: [c] })
    expect(result.entries).toEqual([{ name: 'c.dat', size: 42, file: c }])
    expect(result.skipped).toBe(0)
  })

  it('empty drop is empty with no skip', () => {
    const result = collectFiles({ files: [] })
    expect(result.entries).toEqual([])
    expect(result.skipped).toBe(0)
  })
})

describe('pastedImages', () => {
  it('stages a pasted image as one entry named pasted-image.png (AC-001-3)', () => {
    const clip = file('clip.png', 512)
    const result = pastedImages([{ type: 'image/png', getAsFile: () => clip }])
    expect(result).toEqual([{ name: 'pasted-image.png', size: 512, file: clip }])
  })

  it('ignores non-image clipboard slots', () => {
    const result = pastedImages([{ type: 'text/plain', getAsFile: () => file('t.txt', 8) }])
    expect(result).toEqual([])
  })

  it('ignores image slots without a usable File', () => {
    const result = pastedImages([{ type: 'image/png' }])
    expect(result).toEqual([])
  })

  it('empty clipboard yields nothing', () => {
    expect(pastedImages([])).toEqual([])
  })
})

describe('DropZone markup', () => {
  it('renders the drop zone with the required ARIA and copy (UI §4.1)', () => {
    const html = renderToStaticMarkup(<DropZone />)
    expect(html).toContain('role="button"')
    expect(html).toContain('aria-label="Upload files"')
    expect(html).toContain('<h1 class="dz-title">Send your files.</h1>')
  })

  it('renders the file picker with multiple and its own aria label', () => {
    const html = renderToStaticMarkup(<DropZone />)
    expect(html).toContain('type="file"')
    expect(html).toContain('multiple=""')
    expect(html).toContain('aria-label="Upload files"')
  })
})
