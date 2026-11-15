import { describe, it, expect } from 'vitest'
import { resolveTheme } from './resolveTheme'

// T-015-01 (F-TRF-015 test plan): bootstrap resolution order — account > preference > default.
describe('resolveTheme', () => {
  describe('explicit account choice wins over the OS in both directions (EC-015-1)', () => {
    it("stored 'dark' + light OS → 'dark'", () => {
      expect(resolveTheme('dark', false)).toBe('dark')
    })

    it("stored 'light' + dark OS → 'light'", () => {
      expect(resolveTheme('light', true)).toBe('light')
    })
  })

  describe("'system' follows the OS preference", () => {
    it("stored 'system' + dark OS → 'dark'", () => {
      expect(resolveTheme('system', true)).toBe('dark')
    })

    it("stored 'system' + light OS → 'light'", () => {
      expect(resolveTheme('system', false)).toBe('light')
    })
  })

  describe("guest / absent stored theme (AC-015-2)", () => {
    it("undefined + dark OS → 'dark' — guest on a dark OS loads the landing dark without any API call", () => {
      expect(resolveTheme(undefined, true)).toBe('dark')
    })

    it("undefined + light OS → 'light' (default when no preference matches)", () => {
      expect(resolveTheme(undefined, false)).toBe('light')
    })
  })
})
