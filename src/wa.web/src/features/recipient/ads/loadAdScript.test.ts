// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'

import { loadAdScript, resetAdScriptsForTest } from './loadAdScript'

const SRC = 'https://ads.example.test/ad.js'
const scripts = () => document.head.querySelectorAll<HTMLScriptElement>('script[data-ad-loader]')

afterEach(() => resetAdScriptsForTest())

describe('loadAdScript (TA-8.4)', () => {
  it('appends exactly one deferred async script', () => {
    void loadAdScript(SRC).catch(() => {})
    expect(scripts()).toHaveLength(1)
    const s = scripts()[0]
    expect(s.src).toBe(SRC)
    expect(s.defer).toBe(true)
    expect(s.async).toBe(true)
  })

  it('de-dupes the same src', () => {
    const a = loadAdScript(SRC)
    const b = loadAdScript(SRC)
    expect(b).toBe(a)
    expect(scripts()).toHaveLength(1)
  })

  it('resolves on load', async () => {
    const p = loadAdScript(SRC)
    scripts()[0].dispatchEvent(new Event('load'))
    await expect(p).resolves.toBeUndefined()
  })

  it('rejects on error, removes the tag and allows a retry', async () => {
    const p = loadAdScript(SRC)
    scripts()[0].dispatchEvent(new Event('error'))
    await expect(p).rejects.toThrow()
    expect(scripts()).toHaveLength(0)
    void loadAdScript(SRC).catch(() => {})
    expect(scripts()).toHaveLength(1)
  })
})
