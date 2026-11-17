#!/usr/bin/env node
/**
 * T-017-03 (F-TRF-017, FR-017-4 / FR-017-5): phone touch-target + no-hover gate for P0 surfaces.
 *
 * Static, not layout: checks the DECLARED CSS of every interactive element found in P0 TSX at a phone viewport
 * (rules outside any @media + rules inside `@media (max-width: Npx)` with N >= 360; last declaration per class wins).
 * Rules (all GATE, exit 1):
 *   1. height floor  - max(min-height, height) over the element's classes >= 44 px; no class may declare height < 44 px.
 *   2. width floor   - an explicit width/min-width < 44 px with no class giving >= 44 px.
 *   3. font floor    - input/select/textarea must declare font-size >= 16 px (iOS focus zoom).
 *   4. hover reveal  - a :hover rule must not set display / visibility, opacity: 0, or opacity >= 1 (reveal) (FR-017-5).
 *   5. unclassed interactive element -> cannot verify.
 * Limitations: ignores selector specificity / ancestors; descendant/compound selectors are credited to their last class;
 *   capitalised JSX components are not scanned (their rendered host element is); runtime computed styles are T-017-04's Playwright job.
 * Run from repo root `node scripts/check-tap-targets.mjs` or from src/wa.web via `npm run lint`.
 */

import { readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const WEB = fileURLToPath(new URL('../src/wa.web/', import.meta.url))
// Mirrors the P0 file set of eslint.config.js (T-014-03).
const SURFACES = [
  'src/features/landing', 'src/features/sender', 'src/features/recipient', 'src/features/auth',
  'src/features/files', 'src/features/profile', 'src/features/errors', 'src/core/TopBar.tsx', 'src/App.tsx',
]
// Order of main.tsx imports (later wins).
const STYLESHEETS = ['src/index.css', 'src/features/recipient/recipient.css', 'src/features/errors/errorMap.css']
const ALLOW = {
  dz: 'drop-zone surface (role=button): >= 44 px from padding + content, not min-height',
}
const MIN = 44
const MIN_FONT = 16
const MIN_VIEWPORT = 360

// ---------- CSS ----------
const stripComments = (s) => s.replace(/\/\*[\s\S]*?\*\//g, '')

/** Returns [{selectors, decls}] for rules applicable at a phone viewport, in source order. */
function parseCss(source) {
  const css = stripComments(source)
  const out = []
  function parseBlock(from, end, applicable) {
    let i = from
    while (i < end) {
      const open = css.indexOf('{', i)
      if (open < 0 || open >= end) break
      const head = css.slice(i, open).trim()
      let depth = 1
      let j = open + 1
      while (j < end && depth > 0) {
        if (css[j] === '{') depth++
        else if (css[j] === '}') depth--
        j++
      }
      if (head.startsWith('@')) {
        if (/^@media/.test(head)) {
          const m = /max-width:\s*(\d+)px/.exec(head)
          const ok = applicable && m && Number(m[1]) >= MIN_VIEWPORT && !/min-width/.test(head)
          parseBlock(open + 1, j - 1, Boolean(ok))
        }
      } else if (applicable) {
        const decls = {}
        for (const d of css.slice(open + 1, j - 1).split(';')) {
          const c = d.indexOf(':')
          if (c > 0) decls[d.slice(0, c).trim().toLowerCase()] = d.slice(c + 1).trim()
        }
        out.push({ selectors: head.split(',').map((s) => s.trim()), decls })
      }
      i = j
    }
  }
  parseBlock(0, css.length, true)
  return out
}

const rules = STYLESHEETS.flatMap((f) => parseCss(readFileSync(path.join(WEB, f), 'utf8')))

const vars = {}
for (const r of rules) {
  if (r.selectors.includes(':root')) for (const [k, v] of Object.entries(r.decls)) if (k.startsWith('--')) vars[k] = v
}
function px(value) {
  if (value === undefined) return undefined
  const v = /^var\((--[\w-]+)\)$/.exec(value.trim())
  const resolved = v ? vars[v[1]] : value.trim()
  const m = /^(\d+(?:\.\d+)?)px$/.exec(resolved ?? '')
  return m ? Number(m[1]) : undefined
}

/** class -> prop -> last-wins raw value. */
const byClass = new Map()
const violations = []
for (const r of rules) {
  for (const sel of r.selectors) {
    if (sel.includes(':hover')) {
      const d = r.decls
      if ('display' in d || 'visibility' in d || /^0(\.0+)?$/.test(d.opacity ?? '') || ('opacity' in d && Number.parseFloat(d.opacity) >= 1)) {
        violations.push(`css: "${sel}" reveals/hides content on :hover (FR-017-5)`)
      }
      continue
    }
    const last = sel.split(/[\s>+~]+/).pop()
    const m = /^\.([\w-]+)$/.exec(last)
    if (!m) continue
    const props = byClass.get(m[1]) ?? {}
    Object.assign(props, r.decls)
    byClass.set(m[1], props)
  }
}

// ---------- TSX ----------
function* walk(p) {
  if (statSync(p).isFile()) {
    yield p
    return
  }
  for (const e of readdirSync(p, { withFileTypes: true })) {
    if (e.name === '__lint-fixtures__') continue
    yield* walk(path.join(p, e.name))
  }
}

/** Finds the end index of a JSX opening tag, tracking {} depth and quotes (attributes contain `=>`). */
function tagEnd(src, from) {
  let depth = 0
  let quote = null
  for (let k = from; k < src.length; k++) {
    const c = src[k]
    if (quote) {
      if (c === '\\') k++
      else if (c === quote) quote = null
      continue
    }
    if (c === '"' || c === "'" || c === '`') quote = c
    else if (c === '{') depth++
    else if (c === '}') depth--
    else if (c === '>' && depth === 0) return k
  }
  return -1
}

function classesOf(tag) {
  const m = /\bclassName=/.exec(tag)
  if (!m) return []
  const rest = tag.slice(m.index + m[0].length)
  let expr
  if (rest[0] === '"') expr = rest.slice(1, rest.indexOf('"', 1))
  else if (rest[0] === '{') {
    let depth = 0
    let k = 0
    for (; k < rest.length; k++) {
      if (rest[k] === '{') depth++
      else if (rest[k] === '}' && --depth === 0) break
    }
    expr = rest.slice(1, k)
  } else return []
  // Keep string-literal text only, dropping ${...} interpolations.
  const lits = [...expr.matchAll(/(["'`])((?:\\.|(?!\1)[^\\])*)\1/g)].map((x) => x[2].replace(/\$\{[^}]*\}/g, ' '))
  const text = /["'`]/.test(expr) ? lits.join(' ') : expr
  return text.split(/\s+/).filter((t) => /^[a-z][\w-]*$/.test(t))
}

let count = 0
for (const s of SURFACES) {
  for (const file of walk(path.join(WEB, s))) {
    if (!file.endsWith('.tsx') || /\.test\.tsx$/.test(file)) continue
    const src = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, (c) => c.replace(/[^\n]/g, ' '))
    for (const m of src.matchAll(/(?<![\w$])<([a-z][a-z0-9]*)(?=[\s/>])/g)) {
      const end = tagEnd(src, m.index + m[0].length)
      if (end < 0) continue
      const tag = src.slice(m.index, end + 1)
      const name = m[1]
      const role = /\brole="(button|tab|menuitem)"/.test(tag)
      if (!['button', 'a', 'input', 'select', 'textarea'].includes(name) && !role) continue
      const type = /\btype="([\w-]+)"/.exec(tag)?.[1]
      if (name === 'input' && (type === 'file' || type === 'hidden')) continue
      count++
      const line = src.slice(0, m.index).split('\n').length
      const where = `${path.relative(WEB, file).replaceAll('\\', '/')}:${line} <${name}>`
      const classes = classesOf(tag)
      if (classes.length === 0) {
        violations.push(`${where} unclassed interactive element - cannot verify (add a class or allowlist)`)
        continue
      }
      const label = `${where} class="${classes.join(' ')}"`
      const prop = (p) => classes.map((c) => px(byClass.get(c)?.[p]))
      const allowed = classes.some((c) => c in ALLOW)
      if (!allowed) {
        const heights = [...prop('min-height'), ...prop('height')].filter((v) => v !== undefined)
        if (!heights.some((v) => v >= MIN)) {
          violations.push(`${label} height ${heights.length ? Math.max(...heights) : 'undeclared'} px < ${MIN} px`)
        }
        for (const c of classes) {
          const h = px(byClass.get(c)?.height)
          if (h !== undefined && h < MIN) violations.push(`${label} .${c} declares height ${h} px < ${MIN} px`)
        }
        const widths = [...prop('width'), ...prop('min-width')].filter((v) => v !== undefined)
        if (widths.length && !widths.some((v) => v >= MIN)) {
          violations.push(`${label} explicit width ${Math.max(...widths)} px < ${MIN} px`)
        }
      }
      const isField =
        (name === 'input' && !['checkbox', 'radio', 'submit', 'button'].includes(type)) || name === 'select' || name === 'textarea'
      if (isField) {
        const fonts = classes.map((c) => px(byClass.get(c)?.['font-size'])).filter((v) => v !== undefined)
        if (!fonts.length || Math.min(...fonts) < MIN_FONT) {
          violations.push(`${label} font-size ${fonts.length ? Math.min(...fonts) : 'undeclared'} px < ${MIN_FONT} px (iOS focus zoom)`)
        }
      }
    }
  }
}

if (violations.length > 0) {
  for (const v of violations) console.error(v)
  console.error(`FAIL - ${violations.length} tap-target violation(s) (T-017-03).`)
  process.exit(1)
}
console.log(`PASS - ${count} interactive elements on P0 surfaces meet the ${MIN} px / ${MIN_FONT} px floor (T-017-03).`)
