#!/usr/bin/env node
/**
 * T-016-06 (F-TRF-016, FR-016-4 / AC-016-4): contrast CI check.
 *
 * Parses src/wa.web/src/styles/tokens.css (:root light default + html[data-theme='dark']) and computes the
 * WCAG 2.x relative-luminance contrast ratio for every text-on-background token pair in BOTH themes:
 *   - GATE tier (4.5:1 both themes): --fg, --fg-muted on --bg and --bg-subtle — body/label text everywhere;
 *     a new bad core pair fails CI before merge (US-016-03 Edge cases).
 *   - REPORT tier: --accent/--danger/--success/--warning (status text) + --fg-faint (weakest-token,
 *     placeholders/disabled only per F-TRF-016 UI notes) — printed with OK / AA-large(<4.5) / FAIL flags so the
 *     audit is re-runnable, not one-shot; light-theme status values below 4.5 are the known T-015-03 contract.
 *   - EXCEPTIONS: hardcoded #FFFFFF button text on --accent/--danger (index.css L97–101 documented contract —
 *     white-on-accent/danger, measured contrast below WCAG AA in both palettes; F-TRF-016 owns the P0-surface a11y pass) —
 *     asserted against the measured ratios so a token change that breaks the documented contract fails loudly.
 * Prints the full ratio table; exits 0 on pass, 1 listing every gated failure + exception diff.
 */

import { readFileSync } from 'node:fs'
import process from 'node:process'

const TOKENS_CSS = new URL('../src/wa.web/src/styles/tokens.css', import.meta.url)

/** GATE tier (FR-016-4): body/label text — must be ≥ 4.5:1 in BOTH themes or CI fails before merge. */
const GATED_PAIRS = [
  ['--fg', '--bg'],
  ['--fg', '--bg-subtle'],
  ['--fg-muted', '--bg'],
  ['--fg-muted', '--bg-subtle'],
]

/** REPORT tier: status text + weakest token — printed with OK / AA-large(<4.5) / FAIL flags, never fails CI by itself. */
const REPORT_PAIRS = [
  ['--accent', '--bg'],
  ['--accent', '--bg-subtle'],
  ['--danger', '--bg'],
  ['--danger', '--bg-subtle'],
  ['--success', '--bg'],
  ['--success', '--bg-subtle'],
  ['--warning', '--bg'],
  ['--warning', '--bg-subtle'],
  ['--fg-faint', '--bg'],
  ['--fg-faint', '--bg-subtle'],
]

/**
 * Hardcoded #FFFFFF button text (index.css L100–101 documented contract, T-015-03 comment): not a token pair — the script
 * carries it as a fixed exception list with measured ratios so a regression in the accent/danger TOKEN values surfaces as
 * an exception-list diff. Ratios are recomputed from tokens.css; asserted within ±0.05 of these documented values.
 */
const EXCEPTIONS = [
  { label: 'white on --accent (btn-primary / rp-password-submit)', fg: '#FFFFFF', bgToken: '--accent' },
  { label: 'white on --danger (danger button text contract)', fg: '#FFFFFF', bgToken: '--danger' },
]

/** WCAG 2.x relative luminance from a #RRGGBB hex string. */
function relLuminance(hex) {
  const h = hex.replace('#', '')
  return [0, 1, 2].map((i) => parseInt(h.slice(i * 2, i * 2 + 2), 16) / 255).reduce(
    (sum, v, i) => {
      const c = v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4)
      return sum + [0.2126, 0.7152, 0.0722][i] * c
    },
    0,
  )
}

/** WCAG contrast ratio (fg on bg), always ≥ 1. */
function contrast(fgHex, bgHex) {
  const l = [relLuminance(fgHex), relLuminance(bgHex)].sort((a, b) => a - b)
  return (l[1] + 0.05) / (l[0] + 0.05)
}

/**
 * Extracts { tokenName: value } for each scope in tokens.css — same approach as check-theme-tokens.mjs's parseScopes():
 * strip block comments first (tokens.css carries per-token notes in them), then read the top-level rule blocks whose
 * selector list contains ':root' or "html[data-theme='dark']" / "html[data-theme='light']".
 */
function parseScopes(css) {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const scopes = {}
  for (const match of source.matchAll(/^([^{}]+)\{([\s\S]*?)\}/gm)) {
    const selectorList = match[1].trim()
    const declarations = match[2]
    const tokens = Object.fromEntries(
      [...declarations.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]),
    )
    if (Object.keys(tokens).length === 0) continue // non-token blocks never land here — this file is colors only
    for (const selector of selectorList.split(',')) {
      const sel = selector.trim()
      let scopeName = null
      if (/html\[data-theme='dark'\]/.test(sel)) scopeName = 'dark'
      else if (/html\[data-theme='light'\]/.test(sel)) scopeName = 'light'
      else if (sel === ':root') scopeName = 'root' // the plain :root block is the light default
      if (scopeName) scopes[scopeName] = tokens
    }
  }
  return scopes
}

const css = readFileSync(TOKENS_CSS, 'utf8')
const scopes = parseScopes(css)
// Both themes: ':root' is the light default; html[data-theme='light'] must equal it (T-015-03 parity check owns that).
const THEMES = [
  ['light', scopes.root],
  ['dark', scopes.dark],
]

let failures = []
for (const theme of ['root', 'dark']) {
  if (!scopes[theme]) failures.push(`missing entire ${theme === 'root' ? ':root' : "html[data-theme='dark']"} block in tokens.css`)
}

// GATE tier: every core pair must be ≥ 4.5:1 in both themes (FR-016-4; US-016-03 Edge cases).
for (const [theme, scope] of THEMES) {
  for (const [fgToken, bgToken] of GATED_PAIRS) {
    const fg = scope?.[fgToken]
    const bg = scope?.[bgToken]
    if (!fg || !bg) continue // missing tokens are check-theme-tokens.mjs's failure — don't double-report here.
    const ratio = contrast(fg, bg)
    if (ratio < 4.5) failures.push(`${theme}: ${fgToken} on ${bgToken} is ${ratio.toFixed(2)}:1 (< 4.5:1 gate)`)
  }
}

// EXCEPTIONS: recompute white-on-accent/danger from tokens.css; assert within ±0.05 of the documented contract values.
for (const [theme, scope] of THEMES) {
  for (const ex of EXCEPTIONS) {
    const bg = scope?.[ex.bgToken]
    if (!bg) continue // missing token → check-theme-tokens.mjs owns that failure.
    const ratio = contrast(ex.fg, bg)
    const documented = theme === 'light' ? (ex.bgToken === '--accent' ? 3.57 : 4.4) : ex.bgToken === '--accent' ? 2.81 : 3.36
    if (Math.abs(ratio - documented) > 0.05) {
      failures.push(
        `${theme}: ${ex.label} is ${ratio.toFixed(2)}:1 — the documented contract value is ~${documented}:1 ` +
          `(index.css L97–101; a token change broke the measured-contrast comment or the contract needs re-documenting)`,
      )
    }
  }
}

// Full ratio table (re-runnable audit — AC-016-4 gherkin 3).
console.log('Contrast check (T-016-06, FR-016-4 / AC-016-4):')
for (const [theme] of THEMES) {
  console.log(`\n${theme === 'light' ? ':root (light default)' : "html[data-theme='dark']"} — GATE tier (≥ 4.5:1 required, CI fails below):`)
  for (const [fgToken, bgToken] of GATED_PAIRS) {
    const ratio = contrast(scopes[theme === 'light' ? 'root' : theme][fgToken], scopes[theme === 'light' ? 'root' : theme][bgToken])
    console.log(`  ${fgToken.padEnd(12)} on ${bgToken.padEnd(12)} ${ratio.toFixed(2).padStart(6)}:1  OK`)
  }
  console.log(`${theme === 'light' ? ':root (light default)' : "html[data-theme='dark']"} — REPORT tier (OK / AA-large(<4.5) / FAIL flags, no CI failure):`)
  for (const [fgToken, bgToken] of REPORT_PAIRS) {
    const ratio = contrast(scopes[theme === 'light' ? 'root' : theme][fgToken], scopes[theme === 'light' ? 'root' : theme][bgToken])
    const flag = ratio >= 4.5 ? 'OK' : ratio >= 3.0 ? 'AA-large(<4.5)' : 'FAIL'
    console.log(`  ${fgToken.padEnd(12)} on ${bgToken.padEnd(12)} ${ratio.toFixed(2).padStart(6)}:1  ${flag}`)
  }
}

if (failures.length > 0) {
  console.error('\nFAIL — contrast gate broken:')
  for (const f of failures) console.error(`  - ${f}`)
  process.exit(1)
}
console.log(
  `\nPASS — all ${GATED_PAIRS.length * THEMES.length} gated core pairs ≥ 4.5:1 in both themes; ` +
    `${EXCEPTIONS.length * THEMES.length} documented white-text exceptions match the measured ratios (±0.05).`,
)
