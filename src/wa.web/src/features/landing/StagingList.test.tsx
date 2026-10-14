// @vitest-environment jsdom
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { act } from 'react-dom/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'

import StagingList from './StagingList'
import { useUploadEngine } from '../../core/upload/UploadEngine'
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
})
