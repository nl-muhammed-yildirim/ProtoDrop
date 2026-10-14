// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'

import StagingList from './StagingList'
import { useUploadEngine } from '../../core/upload/UploadEngine'

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

beforeEach(() => {
  useUploadEngine.getState().reset()
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
})
