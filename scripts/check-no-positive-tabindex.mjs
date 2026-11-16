#!/usr/bin/env node
/**
 * T-016-03 (F-TRF-016): no positive tabIndex in src/features/.
 *
 * Positive tabIndexes break the document-order focus model — route-change focus and modal traps
 * rely on natural DOM order, so features code must never use tabIndex={N} with N > 0.
 * tabIndex={-1} (programmatic-focus-only) is allowed; the regex below deliberately does not match it.
 * Mirrors check-theme-tokens.mjs: run from repo root as `node scripts/check-no-positive-tabindex.mjs`.
 */

import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const FEATURES_DIR = fileURLToPath(new URL('../src/wa.web/src/features/', import.meta.url))

/** Walks a directory recursively and yields every .tsx file's absolute path. */
function* walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) yield* walk(full)
    else if (entry.isFile() && entry.name.endsWith('.tsx')) yield full
  }
}

// Matches tabIndex={5}, tabIndex="3", tabindex: "2" etc. — any positive integer after the attribute name.
// tabIndex={-1} / {0} do NOT match (the character class starts at [1-9]).
const POSITIVE_TABINDEX = /tab[Ii]ndex=(\{?)([1-9]\d*)/

let violations = 0
for (const file of walk(FEATURES_DIR)) {
  const lines = readFileSync(file, 'utf8').split('\n')
  for (let i = 0; i < lines.length; i++) {
    if (POSITIVE_TABINDEX.test(lines[i])) {
      violations++
      console.error(`${path.relative(FEATURES_DIR, file)}:${i + 1}: ${lines[i].trim()}`)
    }
  }
}

if (violations > 0) {
  process.exit(1)
}
console.log('PASS — no positive tabIndex in src/features/ (T-016-03).')
