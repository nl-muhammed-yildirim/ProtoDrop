import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

// EC-024-1 / UI-Reference §5.3 CSS contract: jsdom applies no media queries, so read the CSS text.
const css = readFileSync(fileURLToPath(new URL('./adSlot.css', import.meta.url)), 'utf8')

function body(source: string, selector: string): string {
  const at = source.indexOf(`\n${selector} {`)
  if (at < 0) throw new Error(`rule not found: ${selector}`)
  return source.slice(source.indexOf('{', at) + 1, source.indexOf('}', at))
}

function media(source: string, query: string): string {
  const open = source.indexOf(`@media ${query} {`)
  if (open < 0) throw new Error(`media not found: ${query}`)
  let depth = 0
  for (let i = source.indexOf('{', open); i < source.length; i++) {
    if (source[i] === '{') depth++
    else if (source[i] === '}' && --depth === 0) return source.slice(open, i + 1)
  }
  throw new Error('unbalanced media block')
}

describe('adSlot.css contract', () => {
  it('slot is clipped and isolated so an oversized ad cannot cover the page (EC-024-1)', () => {
    const r = body(css, '.rp-ad')
    expect(r).toContain('overflow: hidden')
    expect(r).toContain('isolation: isolate')
  })

  it('state rules', () => {
    expect(body(css, ".rp-ad[data-state='idle']")).toContain('display: none')
    // EC-024-1: loading reserve equals the filled height per breakpoint, so a fill never shifts layout.
    const loading = body(css, ".rp-ad[data-state='loading']")
    expect(loading).toContain('min-height: 90px')
    expect(loading).toContain('max-height: 90px')
    expect(body(css, ".rp-ad[data-state='filled']")).toContain('height: 90px')
    const m = media(css, '(max-width: 640px)')
    expect(m).toContain(".rp-ad[data-state='loading']")
    expect(m.slice(m.indexOf(".rp-ad[data-state='loading']"))).toMatch(/min-height: 100px;\s*max-height: 100px/)
    const c = body(css, ".rp-ad[data-state='collapsed']")
    expect(c).toContain('height: 0')
    expect(c).toContain('pointer-events: none')
  })

  it('the only transition is 200ms ease-out inside prefers-reduced-motion: no-preference', () => {
    expect(css.match(/transition\s*:/g) ?? []).toHaveLength(1)
    const block = media(css, '(prefers-reduced-motion: no-preference)')
    expect(block).toContain('transition')
    expect(block).toContain('200ms ease-out')
  })

  it('desktop unit is 728x90 and phones get 320x100', () => {
    const unit = body(css, '.rp-ad-unit')
    expect(unit).toContain('width: 728px')
    expect(unit).toContain('height: 90px')
    const m = media(css, '(max-width: 640px)')
    expect(m).toContain('width: 320px')
    expect(m).toContain('height: 100px')
  })
  it('US-024-01: the Ad label is an overlay that cannot take clicks', () => {
    const l = body(css, '.rp-ad-label')
    expect(l).toContain('position: absolute')
    expect(l).toContain('pointer-events: none')
    expect(l).toContain('font-size: var(--fs-tiny)')
  })
})
