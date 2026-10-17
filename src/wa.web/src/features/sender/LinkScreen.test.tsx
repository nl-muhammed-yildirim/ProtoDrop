// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

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
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g' }))
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

    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g' }))
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

    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g' }))
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
    const host = renderClient(createElement(LinkScreen, { linkId: 'Xy9zWv3u' }))
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
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g' }))
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
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g' }))
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
    const host = renderClient(createElement(LinkScreen, { linkId: 'Ab3dEf5g' }))
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
})
