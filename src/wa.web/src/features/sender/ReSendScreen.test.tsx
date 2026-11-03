// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import ReSendScreen from './ReSendScreen'

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

function renderClient(el: React.ReactElement): HTMLDivElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => {
    void root.render(el)
  })
  return host
}

// T-010-03 (FR-010-4): endpoint 16 — POST /api/v1/transfers/{id}/resend.
const RESEND_URL = 'http://localhost:8080/api/v1/transfers/a1b2c3d4-e5f6-7890-abcd-ef1234567890/resend'

type ResendResult = {
  id: string
  linkId: string
  status: string
  expiresAt: string
  totalBytes: number
  fileCount: number
  files: Array<{ fileId: string; name: string }>
  recipients: string[]
  note?: string | null
  senderName: string
  senderEmail?: string | null
  passwordRequired: boolean
}

function successResult(): ResendResult {
  return {
    id: 'new-transfer-id',
    linkId: 'NewLink123',
    status: 'Draft',
    expiresAt: '2026-12-01T00:00:00Z',
    totalBytes: 100,
    fileCount: 2,
    files: [{ fileId: 'f1', name: 'a.txt' }, { fileId: 'f2', name: 'b.png' }],
    recipients: ['a@x.com', 'b@y.com'],
    note: 'Original note',
    senderName: 'Ada Lovelace',
    senderEmail: 'ada@example.com',
    passwordRequired: true,
  }
}

function stubFetch(status: number, body: unknown): void {
  vi.stubGlobal(
    'fetch',
    async (input: RequestInfo | URL) => {
      const url = String(input)
      expect(url).toBe(RESEND_URL)
      return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })
    },
  )
}

describe('ReSendScreen', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('(4) Loading state → "Loading…" text before response resolves', async () => {
    let resolveFetch!: (r: Response) => void
    vi.stubGlobal(
      'fetch',
      () => new Promise<Response>((resolve) => { resolveFetch = resolve }),
    )

    const host = renderClient(createElement(ReSendScreen, { originalTransferId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', onUpload: () => {} }))
    expect(host.innerHTML).toContain('Loading…')

    // Resolve with success to clean up.
    await act(async () => {
      resolveFetch(new Response(JSON.stringify(successResult()), { status: 200 }))
    })
    host.remove()
  })

  it('(1) Success → renders LinkScreen with pre-filled values', async () => {
    stubFetch(200, successResult())

    const host = renderClient(createElement(ReSendScreen, { originalTransferId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', onUpload: () => {} }))
    await act(async () => {})

    // Recipients textarea pre-filled.
    const textarea = host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement
    expect(textarea).not.toBeNull()
    expect(textarea.value).toBe('a@x.com\nb@y.com')

    // Note pre-filled.
    const note = host.querySelector('#note-textarea') as HTMLTextAreaElement
    expect(note).not.toBeNull()
    expect(note.value).toBe('Original note')

    // Banner visible with first file + "and 1 more files".
    const banner = host.querySelector('.ls-banner') as HTMLElement
    expect(banner).not.toBeNull()
    expect(banner.textContent).toContain("Re-sending 'a.txt' and 1 more files.")
    host.remove()
  })

  it('(2) FILES_GONE (410 + code "FILES_GONE") → renders "Files were deleted — upload again." + Upload button; no form visible', async () => {
    stubFetch(410, { type: 'about:blank', title: 'Files gone', status: 410, code: 'FILES_GONE' })

    const host = renderClient(createElement(ReSendScreen, { originalTransferId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', onUpload: () => {} }))
    await act(async () => {})

    expect(host.innerHTML).toContain('Files were deleted — upload again.')
    const uploadBtn = host.querySelector('.rs-filesgone-upload') as HTMLButtonElement
    expect(uploadBtn).not.toBeNull()
    expect(uploadBtn.textContent).toBe('Upload')

    // No form visible.
    expect(host.querySelector('.ls-recipients-textarea')).toBeNull()
    expect(host.querySelector('#link-password')).toBeNull()
    host.remove()
  })

  it('(3) Generic error (500) → inline error message + retry button', async () => {
    stubFetch(500, { type: 'about:blank', title: 'Internal Server Error', status: 500 })

    const host = renderClient(createElement(ReSendScreen, { originalTransferId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', onUpload: () => {} }))
    await act(async () => {})

    expect(host.innerHTML).toContain('Failed to load re-send details. Please try again.')
    const retryBtn = host.querySelector('.rs-error-retry') as HTMLButtonElement
    expect(retryBtn).not.toBeNull()
    expect(retryBtn.textContent).toBe('Try again')
    host.remove()
  })
})
