import { describe, expect, it } from 'vitest'
import { API_BASE, DEFAULT_API_BASE, resolveApiBase } from './apiBase'

describe('resolveApiBase (CHORE-deploy-hardening)', () => {
  it.each([undefined, '', '  '])('falls back to the local default for %j', (raw) => {
    expect(resolveApiBase(raw)).toBe(DEFAULT_API_BASE)
  })

  it('strips a trailing slash', () => {
    expect(resolveApiBase('/api/v1/')).toBe('/api/v1')
  })

  it('keeps an absolute base unchanged', () => {
    expect(resolveApiBase('https://x.test/api/v1')).toBe('https://x.test/api/v1')
  })

  it('equals the local default under vitest', () => {
    expect(API_BASE).toBe('http://localhost:8080/api/v1')
  })
})
