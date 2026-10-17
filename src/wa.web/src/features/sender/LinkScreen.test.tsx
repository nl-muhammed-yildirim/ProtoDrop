// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useUploadEngine } from '../../core/upload/UploadEngine'
import { parseRecipients, validateAddress } from './recipients'
import LinkScreen from './LinkScreen'

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

describe('LinkScreen', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders "Your link is ready" heading and the full URL', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    expect(host.innerHTML).toContain('Your link is ready')
    const expectedUrl = `${window.location.origin}/t/Ab3dEf5g`
    expect(host.innerHTML).toContain(expectedUrl)
    host.remove()
  })

  it('clicking Copy puts the URL on clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      writable: true,
    })

    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const btn = host.querySelector('.ls-copy-btn') as HTMLButtonElement
    expect(btn).not.toBeNull()

    await act(async () => {
      btn.click()
    })

    const expectedUrl = `${window.location.origin}/t/Ab3dEf5g`
    expect(writeText).toHaveBeenCalledWith(expectedUrl)
    host.remove()
  })

  it('shows "Copied ✓" for 2 seconds then reverts to "Copy"', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      writable: true,
    })

    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const btn = host.querySelector('.ls-copy-btn') as HTMLButtonElement

    await act(async () => {
      btn.click()
    })

    // After copy: button shows "Copied ✓" and success message is visible.
    expect(btn.textContent).toBe('Copied ✓')
    expect(host.innerHTML).toContain('class="ls-copy-success"')

    // Advance past 2 s: reverts to "Copy".
    act(() => {
      vi.advanceTimersByTime(2000)
    })
    expect(btn.textContent).toBe('Copy')
    expect(host.innerHTML).not.toContain('class="ls-copy-success"')

    host.remove()
  })

  it('the URL has no query parameters or tracking fragments', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Xy9zWv3u', transferId: '1' }))
    const urlEl = host.querySelector('.ls-url') as HTMLElement
    expect(urlEl).not.toBeNull()
    const text = urlEl.textContent!
    // No query string or fragment.
    expect(text).not.toContain('?')
    expect(text).not.toContain('#')
    // Exact shape: origin + /t/ + linkId.
    expect(text).toBe(`${window.location.origin}/t/Xy9zWv3u`)
    host.remove()
  })

  it('renders the recipients textarea with helper text', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const textarea = host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement
    expect(textarea).not.toBeNull()
    expect(host.innerHTML).toContain('One per line — you can also separate with commas.')
    host.remove()
  })

  it('parses comma-separated addresses and ignores trailing spaces', () => {
    const parsed = parseRecipients('a@x.com, b@y.com ')
    expect(parsed).toEqual(['a@x.com', 'b@y.com'])
    expect(validateAddress('a@x.com')).toBe(true)
    expect(validateAddress('b@y.com')).toBe(true)
  })

  it('shows a per-address inline error for invalid addresses while valid ones proceed', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const textarea = host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement

    const nativeSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!
    act(() => {
      nativeSetter.call(textarea, 'bad-address\nok@x.com')
      textarea.dispatchEvent(new Event('input', { bubbles: true }))
    })

    act(() => {
      textarea.dispatchEvent(new FocusEvent('blur'))
    })

    const errors = host.querySelectorAll('.ls-recipients-error')
    expect(errors.length).toBe(1)
    expect(errors[0].textContent).toBe('bad-address')
    host.remove()
  })

  it('shows an inline cap error when more than 20 unique valid addresses are entered', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const textarea = host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement

    const nativeSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!
    act(() => {
      nativeSetter.call(textarea, Array.from({ length: 25 }, (_, i) => `user${i}@x.com`).join('\n'))
      textarea.dispatchEvent(new Event('input', { bubbles: true }))
    })

    act(() => {
      textarea.dispatchEvent(new FocusEvent('blur'))
    })

    const errors = host.querySelectorAll('.ls-recipients-error')
    expect(errors.length).toBe(1)
    expect(errors[0].textContent).toContain('Maximum 20 recipients')
    host.remove()
  })

  // ---- T-002-07: sender name, email, and note fields ----

  it('renders From field pre-filled with senderName prop', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', senderName: 'Ada Lovelace', transferId: '1' }))
    const input = host.querySelector('#from-name') as HTMLInputElement
    expect(input).not.toBeNull()
    expect(input.value).toBe('Ada Lovelace')
    host.remove()
  })

  it('guest with empty name renders empty From field', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const input = host.querySelector('#from-name') as HTMLInputElement
    expect(input).not.toBeNull()
    expect(input.value).toBe('')
    host.remove()
  })

  it('note counter shows count/500 in --fs-tiny', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const textarea = host.querySelector('#note-textarea') as HTMLTextAreaElement
    expect(textarea).not.toBeNull()

    const nativeSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!
    act(() => {
      nativeSetter.call(textarea, 'hello world')
      textarea.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const counter = host.querySelector('.ls-char-counter') as HTMLElement
    expect(counter).not.toBeNull()
    expect(counter.textContent).toBe('11/500')
    host.remove()
  })

  it('note counter turns warning at 450+ chars', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const textarea = host.querySelector('#note-textarea') as HTMLTextAreaElement
    expect(textarea).not.toBeNull()

    const nativeSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!
    act(() => {
      nativeSetter.call(textarea, 'a'.repeat(450))
      textarea.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const counter = host.querySelector('.ls-char-counter') as HTMLElement
    expect(counter).not.toBeNull()
    expect(counter.className).toContain('ls-char-counter--warning')
    host.remove()
  })

  it('invalid sender email shows inline error without blocking send', () => {
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
    const emailInput = host.querySelector('#sender-email') as HTMLInputElement
    expect(emailInput).not.toBeNull()

    const nativeSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
    act(() => {
      nativeSetter.call(emailInput, 'not-an-email')
      emailInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    act(() => {
      emailInput.dispatchEvent(new FocusEvent('blur'))
    })

    const error = host.querySelector('.ls-sender-error') as HTMLElement
    expect(error).not.toBeNull()
    expect(error.textContent).toContain('valid email')
    host.remove()
  })

  // ---- T-002-08: Copy link only + confirmation screen (FR-002-6/7, AC-002-3) ----

  type SendCall = { url: string; method: string; headers: Record<string, string>; body: string }

  // Local copy of the StagingList stub-fetch pattern (same harness rules — no cross-file coupling).
  function stubSendFetch(status: number): SendCall[] {
    const calls: SendCall[] = []
    vi.stubGlobal(
      'fetch',
      async (input: RequestInfo | URL, init?: RequestInit) => {
        calls.push({
          url: String(input),
          method: typeof init?.method === 'string' ? init.method : 'GET',
          headers: (init?.headers as Record<string, string>) ?? {},
          body: typeof init?.body === 'string' ? init.body : '',
        })
        const problemJson = status < 400
          ? { id: '1', linkId: 'Ab3dEf5g', status: '1', expiresAt: '2026-01-01T00:00:00Z', totalBytes: 0, fileCount: 0, recipients: [] as string[] }
          : { type: 'about:blank', title: 'Not found', status: 404 } // Problem+JSON on the error path
        return new Response(JSON.stringify(problemJson), { status, headers: { 'Content-Type': 'application/problem+json' } })
      },
    )
    return calls
  }

  const setRecipients = (host: HTMLDivElement, value: string): void => {
    const textarea = host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement
    const nativeSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!
    act(() => {
      nativeSetter.call(textarea, value)
      textarea.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  const findButton = (host: HTMLDivElement, text: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent === text)

  describe('Copy link only + confirmation', () => {
    beforeEach(() => { useUploadEngine.getState().reset() })
    afterEach(() => { useUploadEngine.getState().reset() }) // module-level store cleanup for tests 4–5.

    it('renders "Copy link only" ghost button next to Send transfer', () => {
      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      const copyOnly = host.querySelector('.ls-copyonly-btn') as HTMLButtonElement
      expect(copyOnly).not.toBeNull()
      expect(copyOnly.textContent).toBe('Copy link only')
      expect(findButton(host, 'Send transfer')).toBeDefined()
      host.remove()
    })

    it('AC-002-3: Copy link only POSTs emails [] with an Idempotency-Key and confirms in place', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, writable: true })
      const calls = stubSendFetch(201)

      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      const copyOnly = host.querySelector('.ls-copyonly-btn') as HTMLButtonElement

      await act(async () => {
        copyOnly.click()
      })

      expect(calls).toHaveLength(1)
      expect(calls[0].url).toBe('http://localhost:8080/api/v1/transfers/1/send')
      expect(calls[0].method).toBe('POST')
      expect(calls[0].headers['Idempotency-Key']).toBeTruthy()
      const body = JSON.parse(calls[0].body) as { emails: string[] }
      expect(body.emails).toEqual([])

      expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/t/Ab3dEf5g`)
      expect(host.innerHTML).toContain('Transfer sent.')
      // Form phase gone — recipients textarea (and the whole form) swapped out.
      expect(host.querySelector('#recipients-textarea')).toBeNull()
      host.remove()
    })

    it('whitespace-only recipients field sends emails [] via the same link-only path', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, writable: true })
      const calls = stubSendFetch(201)

      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      setRecipients(host, '  \n\t ')
      const copyOnly = host.querySelector('.ls-copyonly-btn') as HTMLButtonElement

      await act(async () => {
        copyOnly.click()
      })

      const body = JSON.parse(calls[0].body) as { emails: string[] }
      expect(body.emails).toEqual([])
      expect(host.querySelectorAll('.ls-recipients-error').length).toBe(0)
      expect(host.innerHTML).toContain('Transfer sent.')
      host.remove()
    })

    it('Send transfer posts the parsed emails and lands on the same confirmation', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, writable: true })
      const calls = stubSendFetch(201)

      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      setRecipients(host, 'a@x.com')
      const sendBtn = findButton(host, 'Send transfer') as HTMLButtonElement

      await act(async () => {
        sendBtn.click()
      })

      const body = JSON.parse(calls[0].body) as { emails: string[] }
      expect(body.emails).toEqual(['a@x.com'])
      expect(host.innerHTML).toContain('Transfer sent.')

      // The confirmation's link-row Copy works.
      const copyBtn = host.querySelector('.ls-copy-btn') as HTMLButtonElement
      await act(async () => {
        copyBtn.click()
      })
      expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/t/Ab3dEf5g`)

      expect(findButton(host, 'Send again')).toBeDefined()
      host.remove()
    })

    it('"Send again" calls onSendAgain with the staged files as {name, size}', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, writable: true })
      stubSendFetch(201)

      // Seed via the store's exported API (same as StagingList tests' stage() helper).
      useUploadEngine.getState().addFiles([{ name: 'a.txt', size: 10 }, { name: 'b.png', size: 5 }])
      const onSendAgain = vi.fn()

      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1', onSendAgain }))
      const copyOnly = host.querySelector('.ls-copyonly-btn') as HTMLButtonElement
      await act(async () => {
        copyOnly.click()
      })

      const sendAgain = findButton(host, 'Send again') as HTMLButtonElement
      await act(async () => {
        sendAgain.click()
      })

      expect(onSendAgain).toHaveBeenCalledTimes(1)
      expect(onSendAgain).toHaveBeenCalledWith([{ name: 'a.txt', size: 10 }, { name: 'b.png', size: 5 }])
      host.remove()
    })

    it('a failed send keeps the form and shows an inline error, no confirmation', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, writable: true })
      stubSendFetch(404)

      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      const copyOnly = host.querySelector('.ls-copyonly-btn') as HTMLButtonElement
      await act(async () => {
        copyOnly.click()
      })

      expect(host.innerHTML).toContain('Your link is ready') // form still visible
      const error = host.querySelector('.ls-send-error') as HTMLElement
      expect(error).not.toBeNull()
      expect(error.getAttribute('role')).toBe('alert')
      expect(host.innerHTML).not.toContain('Transfer sent.')
      host.remove()
    })

    it('a fetch() rejection (network down) keeps the form and shows an inline error, no confirmation', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, writable: true })
      vi.stubGlobal('fetch', () => Promise.reject(new TypeError('Failed to fetch')))

      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      const copyOnly = host.querySelector('.ls-copyonly-btn') as HTMLButtonElement
      await act(async () => {
        copyOnly.click()
      })

      expect(host.innerHTML).toContain('Your link is ready') // form still visible
      const error = host.querySelector('.ls-send-error[role="alert"]') as HTMLElement
      expect(error).not.toBeNull()
      expect(host.innerHTML).not.toContain('Transfer sent.')
      host.remove()
    })

    it('invalid recipients disable both send buttons', () => {
      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      setRecipients(host, 'bad')
      act(() => {
        (host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement).dispatchEvent(new FocusEvent('blur'))
      })

      expect((host.querySelector('.ls-copyonly-btn') as HTMLButtonElement).disabled).toBe(true)
      expect(findButton(host, 'Send transfer')!.disabled).toBe(true)
      host.remove()
    })

    it('exceeding the cap disables both send buttons including Copy link only', () => {
      const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g', transferId: '1' }))
      setRecipients(host, Array.from({ length: 21 }, (_, i) => `user${i}@x.com`).join('\n'))
      act(() => {
        (host.querySelector('.ls-recipients-textarea') as HTMLTextAreaElement).dispatchEvent(new FocusEvent('blur'))
      })

      expect((host.querySelector('.ls-copyonly-btn') as HTMLButtonElement).disabled).toBe(true)
      expect(findButton(host, 'Send transfer')!.disabled).toBe(true)
      host.remove()
    })
  })
})
