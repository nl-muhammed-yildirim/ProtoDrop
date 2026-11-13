#!/usr/bin/env node
/**
 * T-015-03 (F-TRF-015, FR-015-2 / AC-015-4): theme token-diff CI check.
 *
 * Parses src/wa.web/src/styles/tokens.css and extracts the custom properties defined in each of the three scopes —
 * :root (light default), html[data-theme='dark'], html[data-theme='light'] — then asserts:
 *   1. every one of the 14 color tokens is present in ALL THREE scopes (a missing variable per theme fails, FR-015-2);
 *   2. for every token, the dark value differs from the light value (catches copy-paste drift where a "dark"
 *      palette entry was pasted equal to its light twin).
 * Prints a per-token table; exits 0 on pass, 1 with the missing/duplicated names on fail.
 */

import { readFileSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'

const TOKENS_CSS = new URL('../src/wa.web/src/styles/tokens.css', import.meta.url)

/** The full 14-token set (UI-Reference §1 / F-TRF-015's UI notes — the same list tokens.css documents). */
const EXPECTED_TOKENS = [
  '--bg',
  '--bg-subtle',
  '--bg-elevated',
  '--fg',
  '--fg-muted',
  '--fg-faint',
  '--accent',
  '--accent-hover',
  '--accent-soft',
  '--border',
  '--danger',
  '--danger-soft',
  '--success',
  '--warning',
]

/**
 * Extracts { tokenName: value } for each scope in tokens.css. Scopes are the top-level rule blocks whose selector list
 * contains ':root' or "html[data-theme='dark']" / "html[data-theme='light']"; a block may combine selectors (the dark/light
 * scopes pair 'html[...] :root, html[...]' — one declaration set covers both). Comments are stripped first.
 */
function parseScopes(css) {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, '') // strip /* ... */ comments (tokens.css carries per-token notes in them)
  const scopes = {}
  for (const match of source.matchAll(/^([^{}]+)\{([\s\S]*?)\}/gm)) {
    const selectorList = match[1].trim()
    const declarations = match[2]
    const tokens = Object.fromEntries(
      [...declarations.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]),
    )
    if (Object.keys(tokens).length === 0) continue // non-token blocks (:root font-size/radius in index.css never lands here — this file is colors only)
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

let failures = []
for (const scope of ['root', 'dark', 'light']) {
  if (!scopes[scope]) failures.push(`missing entire ${scope === 'root' ? ':root' : `html[data-theme='${scope}']`} block in tokens.css`)
}

// Presence: every token must be defined in all three scopes.
for (const scope of ['root', 'dark', 'light']) {
  const label = scope === 'root' ? ':root (light default)' : `[data-theme='${scope}']`
  for (const name of EXPECTED_TOKENS) {
    if (!(name in scopes[scope] ?? {})) failures.push(`${label}: missing ${name}`)
  }
}

// Drift: dark values must differ from light (:root is the light default).
for (const name of EXPECTED_TOKENS) {
  const light = scopes.root?.[name]
  const dark = scopes.dark?.[name]
  if (light !== undefined && dark !== undefined && light === dark) failures.push(`${name}: dark value equals light (${dark}) — copy-paste drift`)
}

// Per-token table.
console.log('Theme token parity check (T-015-03, FR-015-2):')
for (const name of EXPECTED_TOKENS) {
  const root = scopes.root?.[name] ?? '—'
  const dark = scopes.dark?.[name] ?? '—'
  const light = scopes.light?.[name] ?? '—'
  console.log(`  ${name.padEnd(16)} :root=${String(root).padEnd(9)} dark=${dark} light=${light}`)
}

if (failures.length > 0) {
  console.error('\nFAIL — token parity broken:')
  for (const f of failures) console.error(`  - ${f}`)
  process.exit(1)
}
console.log(`\nPASS — all ${EXPECTED_TOKENS.length} tokens present in :root / dark / light; no dark==light drift.`)
