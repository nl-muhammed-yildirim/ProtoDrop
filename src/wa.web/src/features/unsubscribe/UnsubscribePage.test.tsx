import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'

import UnsubscribePage from './UnsubscribePage'

// T-006-03: the component uses useEffect + useState (fetch on mount), so renderToStaticMarkup
// only captures the initial "Processing…" state. We assert that state directly; the resolved
// card is covered by integration tests (wa.api.integration) and the e2e suite.

describe('UnsubscribePage (T-006-03)', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders loading state (Processing…) before the API resolves', () => {
    const html = renderToStaticMarkup(<UnsubscribePage token="abc.def" />)
    expect(html).toContain('<p class="uns-loading" role="status">Processing…</p>')
  })

  it('does not leak "token invalid" or any error text in the loading state', () => {
    const html = renderToStaticMarkup(<UnsubscribePage token="garbage.token.here" />)
    // No error/404/not-found wording — only the neutral loading message.
    expect(html).not.toContain('invalid')
    expect(html).not.toContain('error')
    expect(html).not.toContain('404')
  })
})
