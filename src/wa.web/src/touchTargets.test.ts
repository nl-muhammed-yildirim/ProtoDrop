import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

// T-017-03 CSS contract: jsdom applies no media queries, so read the stylesheets directly (precedent landingMobile.test.ts).
const read = (rel: string) => readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8')
const css = read('./index.css')
const recipientCss = read('./features/recipient/recipient.css')
const indexHtml = read('../index.html')

function mediaBlock(source: string, marker: string): string {
  const at = source.indexOf(marker)
  if (at < 0) throw new Error(`marker not found: ${marker}`)
  const open = source.indexOf('@media (max-width: 640px) {', at)
  if (open < 0) throw new Error('media block not found')
  let depth = 0
  for (let i = source.indexOf('{', open); i < source.length; i++) {
    if (source[i] === '{') depth++
    else if (source[i] === '}' && --depth === 0) return source.slice(open, i + 1)
  }
  throw new Error('unbalanced media block')
}

/** Declarations of the rule whose selector list contains `selector` (exact, comma-list aware). */
function decls(source: string, selector: string): string {
  const re = /([^{}]+)\{([^{}]*)\}/g
  for (const m of source.matchAll(re)) {
    const sels = m[1].replace(/\/\*[\s\S]*?\*\//g, '').split(',').map((s) => s.trim())
    if (sels.includes(selector)) return m[2]
  }
  throw new Error(`rule not found: ${selector}`)
}

const block = mediaBlock(css, 'T-017-03 (FR-017-4/5')

describe('T-017-03 phone touch-target CSS contract', () => {
  it('avatar is 44x44 (AC-017-3 / FR-017-4)', () => {
    const r = decls(block, '.tb-avatar')
    expect(r).toContain('width: 44px')
    expect(r).toContain('height: 44px')
  })

  it.each(['.tb-signin', '.tb-dropdown-row', '.ls-copy-btn', '.mf-retry'])('%s has min-height 44px', (sel) => {
    expect(decls(block, sel)).toContain('min-height: 44px')
  })

  it('password toggle is at least 44x44', () => {
    const r = decls(block, '.ls-password-toggle')
    expect(r).toContain('min-width: 44px')
    expect(r).toContain('min-height: 44px')
  })

  it.each(['.tb-theme-select', '.ls-sender-input', '.ls-password-input', '.au-input', '.pf-input', '.pf-theme-select'])(
    '%s is 44px tall with 16px font (no iOS focus zoom)',
    (sel) => {
      const r = decls(block, sel)
      expect(r).toContain('min-height: 44px')
      expect(r).toContain('font-size: var(--fs-body)')
    },
  )

  it.each(['.ls-recipients-textarea', '.ls-note-textarea'])('%s uses 16px font', (sel) => {
    expect(decls(block, sel)).toContain('font-size: var(--fs-body)')
  })

  it('no horizontal scroll at 360px: My Files and link screen wrap (AC-017-3)', () => {
    expect(decls(block, '.mf-row')).toContain('flex-wrap: wrap')
    expect(decls(block, '.mf-actions')).toContain('flex-wrap: wrap')
    expect(decls(block, '.mf-actions')).toContain('width: 100%')
    expect(decls(block, '.mf-countdown')).toContain('min-width: 0')
    expect(decls(block, '.ls-actions')).toContain('flex-wrap: wrap')
    expect(decls(block, '.ls-recipients-error')).toContain('overflow-wrap: anywhere')
  })

  it('recipient heading wraps long sender names at phone width', () => {
    const rb = mediaBlock(recipientCss, 'T-003-08')
    expect(decls(rb, '.rp-heading')).toContain('overflow-wrap: anywhere')
  })

  it('desktop is unchanged: the base .ls-copy-btn rule still declares min-height 36px', () => {
    const base = css.slice(0, css.indexOf('T-017-03 (FR-017-4/5'))
    expect(decls(base, '.ls-copy-btn')).toContain('min-height: 36px')
  })
})

describe('EC-017-3 viewport meta', () => {
  it('allows pinch-zoom', () => {
    const meta = /<meta[^>]+name="viewport"[^>]*>/.exec(indexHtml)?.[0] ?? ''
    expect(meta).toContain('width=device-width')
    expect(meta).not.toMatch(/user-scalable\s*=\s*(no|0)/)
    expect(meta).not.toMatch(/maximum-scale\s*=\s*1(\.0)?\b/)
  })
})

describe('check-tap-targets gate', () => {
  it('exits 0 on the current tree', () => {
    const script = fileURLToPath(new URL('../../../scripts/check-tap-targets.mjs', import.meta.url))
    const out = execFileSync(process.execPath, [script], { encoding: 'utf8' })
    expect(out).toContain('PASS')
  })
})
