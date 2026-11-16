import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

// T-017-01 CSS contract: jsdom applies no media queries, so read index.css directly (precedent recipientRoute.test.ts).
const css = readFileSync(fileURLToPath(new URL('../../index.css', import.meta.url)), 'utf8')

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

function rule(source: string, selector: string): string {
  const at = source.indexOf(`  ${selector} {`)
  if (at < 0) throw new Error(`rule not found: ${selector}`)
  return source.slice(source.indexOf('{', at) + 1, source.indexOf('}', at))
}

const block = mediaBlock(css, 'T-017-01')

describe('T-017-01 mobile upload surface CSS contract', () => {
  it('.sl-footer is a viewport-pinned opaque bar with a safe-area inset (FR-017-5)', () => {
    const r = rule(block, '.sl-footer')
    expect(r).toContain('position: fixed')
    expect(r).toContain('bottom: 0')
    expect(r).toContain('env(safe-area-inset-bottom')
    expect(r).toContain('background: var(--bg)')
  })

  it('.sl reserves bottom space >= 96px so the bar never covers the last row', () => {
    const padding = /padding:\s*([^;]+);/.exec(rule(block, '.sl'))?.[1] ?? ''
    const nums = padding.match(/\d+(?=px)/g) ?? []
    expect(Number(nums[nums.length - 1])).toBeGreaterThanOrEqual(96)
  })

  it('.dz drops the tall drag target on phones (FR-017-2)', () => {
    expect(rule(block, '.dz')).toContain('min-height: auto')
  })

  it('row actions stay >= 44px tap targets (FR-017-4)', () => {
    expect(rule(block, '.sl-retry')).toContain('min-height: 44px')
    const base = css.slice(0, css.indexOf('T-017-01'))
    const remove = /\.sl-remove\s*\{([^}]*)\}/.exec(base)?.[1] ?? ''
    expect(remove).toContain('min-width: 44px')
    expect(remove).toContain('min-height: 44px')
  })

  it('.sl-name keeps ellipsis truncation', () => {
    const name = /\.sl-name\s*\{([^}]*)\}/.exec(css)?.[1] ?? ''
    expect(name).toContain('text-overflow: ellipsis')
  })
})
