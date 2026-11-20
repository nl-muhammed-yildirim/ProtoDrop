// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'

import { createAdSenseLoader } from './adsenseLoader'
import { resetAdScriptsForTest } from './loadAdScript'

const CONFIG = { client: 'ca-pub-1234567890123456', unit: '1234567890' }
const scripts = () => document.head.querySelectorAll<HTMLScriptElement>('script[data-ad-loader]')
const flush = () => new Promise((r) => setTimeout(r, 0))

function mount() {
  const container = document.createElement('div')
  document.body.appendChild(container)
  return container
}

afterEach(() => {
  resetAdScriptsForTest()
  document.body.innerHTML = ''
  delete (window as unknown as { adsbygoogle?: unknown }).adsbygoogle
  window.history.replaceState(null, '', '/')
})

describe('createAdSenseLoader', () => {
  it('appends one script whose src carries only the client param', async () => {
    void createAdSenseLoader(CONFIG)(mount()).catch(() => {})
    await flush()
    expect(scripts()).toHaveLength(1)
    const url = new URL(scripts()[0].src)
    expect(url.origin + url.pathname).toBe('https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js')
    expect([...url.searchParams.keys()]).toEqual(['client'])
    expect(url.searchParams.get('client')).toBe(CONFIG.client)
  })

  it('after load: one ins.adsbygoogle with client/slot/page-url, queue length 1', async () => {
    const container = mount()
    void createAdSenseLoader(CONFIG)(container)
    await flush()
    scripts()[0].dispatchEvent(new Event('load'))
    await flush()
    const ins = container.querySelectorAll('ins.adsbygoogle')
    expect(ins).toHaveLength(1)
    expect(ins[0].getAttribute('data-ad-client')).toBe(CONFIG.client)
    expect(ins[0].getAttribute('data-ad-slot')).toBe(CONFIG.unit)
    expect(ins[0].getAttribute('data-page-url')).toBe(`${window.location.origin}/`)
    expect(ins[0].hasAttribute('data-ad-format')).toBe(false)
    expect((window as unknown as { adsbygoogle: unknown[] }).adsbygoogle).toHaveLength(1)
  })

  it('FR-024-5: the linkId never reaches the script src or any ins attribute', async () => {
    window.history.replaceState(null, '', '/t/ab3def5g')
    const container = mount()
    void createAdSenseLoader(CONFIG)(container)
    await flush()
    scripts()[0].dispatchEvent(new Event('load'))
    await flush()
    expect(scripts()[0].src).not.toContain('ab3def5g')
    const ins = container.querySelector('ins')!
    for (const attr of Array.from(ins.attributes)) expect(attr.value).not.toContain('ab3def5g')
  })

  it.each([
    ['filled', 'filled'],
    ['unfilled', 'empty'],
  ])('data-ad-status=%s resolves %s', async (status, expected) => {
    const container = mount()
    const p = createAdSenseLoader(CONFIG)(container)
    await flush()
    scripts()[0].dispatchEvent(new Event('load'))
    await flush()
    container.querySelector('ins')!.setAttribute('data-ad-status', status)
    await expect(p).resolves.toBe(expected)
  })

  it('abort disconnects the MutationObserver and rejects (timeout/unmount cleanup)', async () => {
    const disconnect = vi.spyOn(MutationObserver.prototype, 'disconnect')
    const ctrl = new AbortController()
    const container = mount()
    const p = createAdSenseLoader(CONFIG)(container, ctrl.signal)
    const settled = expect(p).rejects.toMatchObject({ name: 'AbortError' })
    await flush()
    scripts()[0].dispatchEvent(new Event('load'))
    await flush()
    expect(disconnect).not.toHaveBeenCalled()
    ctrl.abort()
    await settled
    expect(disconnect).toHaveBeenCalledTimes(1)
    disconnect.mockRestore()
  })

  it('an already-aborted signal rejects without leaving an observer', async () => {
    const observe = vi.spyOn(MutationObserver.prototype, 'observe')
    const ctrl = new AbortController()
    ctrl.abort()
    const p = createAdSenseLoader(CONFIG)(mount(), ctrl.signal)
    const settled = expect(p).rejects.toMatchObject({ name: 'AbortError' })
    await flush()
    scripts()[0].dispatchEvent(new Event('load'))
    await settled
    expect(observe).not.toHaveBeenCalled()
    observe.mockRestore()
  })

  it('script error rejects', async () => {
    const p = createAdSenseLoader(CONFIG)(mount())
    await flush()
    scripts()[0].dispatchEvent(new Event('error'))
    await expect(p).rejects.toThrow()
  })
})
