#!/usr/bin/env node
/**
 * T-012-05 (F-TRF-012, US-012-01; EC-012-3 / AC-012-3; TA-9.4 PII policy, TA-10.2 telemetry names): telemetry PII guard.
 *
 * Scans the two telemetry exits for raw email patterns and un-hashed / unmarked PII:
 *   server  — WaTelemetryReporter calls (`_telemetry.X(...)`, `telemetry.X(...)`, `_telemetry?.X(...)`) in src/**\/*.cs
 *   client  — trackClientEvent(name, props) calls in src/wa.web/src/**\/*.{ts,tsx} (props are spread verbatim into App Insights)
 *
 * Rules (all gate-tier; any finding exits 1):
 *   R1  email literal      — an address-shaped string (x@y.zz) in a helper call's arguments or in a reporter log template.
 *   R2  PII identifier     — a server helper argument that names email / mail / address / name / ip / password / passwd /
 *                            secret / sas / sig / signature (after stripping string literals, TelemetryEvents.X members and
 *                            slot-wrapped spans PiiTransforms.FileName(..) / PiiTransforms.IpAddress(..) / FileNameSlot(..) / IpSlot(..)).
 *   R3  template property  — every {placeholder} in a WaTelemetryReporter log template must be in SERVER_PROPS (closed set).
 *   R4  string-slot wrap   — an argument to a STRING_SLOT_METHODS member (TransferPageViewed) must be slot-wrapped.
 *   R5  client props       — trackClientEvent's 2nd argument must be an object literal whose keys are all in CLIENT_PROPS
 *                            and whose value expressions name no PII word (a bare identifier / spread variable hides the keys, so it fails).
 *   R6  bypass             — TelemetryClient / .TrackEvent( / .TrackTrace( in src/**\/*.cs, or the App Insights ingestion host in a
 *                            web file other than core/telemetry/clientTelemetry.ts (AC-056-4: the reporter / trackClientEvent are the single exits).
 *
 * Allowlists are CLOSED sets: adding a telemetry property means editing one list below — that diff is the PR-visible review point (EC-012-3).
 *
 * Run (repo root):   node scripts/check-telemetry-pii.mjs            scan the tree; exit 0 = PASS, 1 = findings
 *                    node scripts/check-telemetry-pii.mjs selftest   prove every seeded violation is flagged and every clean shape passes
 *
 * Limitation: a paren/quote-aware text scan, not a Roslyn/TS parser (a helper reached through an alias variable escapes R2; R6 + the
 * reflection pin in WaTelemetryReporterTests mitigate). T-069 owns the [Pii] attribute, event-DTO convention tests and general _logger templates.
 * Node built-ins only (TA-17: no new packages).
 */

import { readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const ROOT = fileURLToPath(new URL('..', import.meta.url))
const SRC = path.join(ROOT, 'src')
const REPORTER_CS = 'src/wa.application/Telemetry/WaTelemetryReporter.cs'
const CLIENT_TELEMETRY_TS = 'src/wa.web/src/core/telemetry/clientTelemetry.ts'
const INGESTION_HOST = 'dc.services.visualstudio.com'

/** Property names the WaTelemetryReporter templates carry (placeholders, exactly). */
const SERVER_PROPS = new Set(['bytes', 'durationMs', 'retries', 'sizeBytes', 'seconds', 'count', 'linkId'])
/** Prop keys passed to trackClientEvent today (exactly). */
const CLIENT_PROPS = new Set(['fileId', 'retries', 'reason', 'bytes', 'durationMs', 'key', 'locale', 'source'])
/** Reporter methods whose string parameter lands in a property — the argument must be slot-wrapped (TA-9.4). */
const STRING_SLOT_METHODS = new Set(['TransferPageViewed'])
/** The slot transforms themselves — their argument IS the raw value being hashed. */
const SLOT_METHODS = new Set(['FileNameSlot', 'IpSlot'])
const PII_WORDS = new Set(['email', 'mail', 'address', 'name', 'ip', 'password', 'passwd', 'secret', 'sas', 'sig', 'signature'])
const EMAIL_RE = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/

// ---------------------------------------------------------------------------------------------------------------------
// Lexing helpers
// ---------------------------------------------------------------------------------------------------------------------

const isEol = (c) => c === '\n' || c === '\r'

/**
 * Returns two strings of the same length as `text` (so indices line up):
 *   noComments — comments blanked, string literals intact (R1 reads literal contents here);
 *   code       — comments AND string contents blanked (quotes kept), so paren/comma/identifier scans never see text inside strings.
 * C# interpolated strings keep their {holes} as code. Regular strings end at end-of-line (guards against a JSX apostrophe desyncing the scan).
 */
function mask(text, lang) {
  const nc = text.split('')
  const cd = text.split('')
  const n = text.length
  const blank = (arr, from, to) => {
    for (let k = from; k < to; k++) if (!isEol(arr[k])) arr[k] = ' '
  }
  let i = 0
  while (i < n) {
    const c = text[i]
    const d = text[i + 1]
    if (c === '/' && d === '/') {
      let j = i
      while (j < n && !isEol(text[j])) j++
      blank(nc, i, j)
      blank(cd, i, j)
      i = j
      continue
    }
    if (c === '/' && d === '*') {
      let j = text.indexOf('*/', i + 2)
      j = j < 0 ? n : j + 2
      blank(nc, i, j)
      blank(cd, i, j)
      i = j
      continue
    }
    if (lang === 'cs') {
      if (c === "'") {
        // char literal: '\x' or 'x'
        if (d === '\\') {
          const j = text.indexOf("'", i + 2)
          i = j < 0 ? i + 1 : j + 1
        } else if (text[i + 2] === "'") i += 3
        else i += 1
        continue
      }
      const m = /^(\$@|@\$|@|\$)?"/.exec(text.slice(i, i + 3))
      if (m && (c === '"' || c === '$' || c === '@')) {
        const prefix = m[1] ?? ''
        const verbatim = prefix.includes('@')
        const interp = prefix.includes('$')
        let j = i + m[0].length
        const bodyStart = j
        while (j < n) {
          const ch = text[j]
          if (verbatim) {
            if (ch === '"') {
              if (text[j + 1] === '"') {
                j += 2
                continue
              }
              break
            }
          } else {
            if (ch === '\\') {
              j += 2
              continue
            }
            if (ch === '"' || isEol(ch)) break
          }
          if (interp && ch === '{') {
            if (text[j + 1] === '{') {
              blank(cd, j, j + 2)
              j += 2
              continue
            }
            let depth = 1
            let k = j + 1
            while (k < n && depth > 0) {
              if (text[k] === '{') depth++
              else if (text[k] === '}') depth--
              k++
            }
            j = k // hole stays visible in `code`
            continue
          }
          blank(cd, j, j + 1)
          j++
        }
        void bodyStart
        i = Math.min(j + 1, n)
        continue
      }
      i++
      continue
    }
    // ts / tsx
    if (c === "'" || c === '"' || c === '`') {
      let j = i + 1
      while (j < n) {
        const ch = text[j]
        if (ch === '\\') {
          j += 2
          continue
        }
        if (ch === c) break
        if (c !== '`' && isEol(ch)) break
        j++
      }
      blank(cd, i + 1, Math.min(j, n))
      i = Math.min(j + 1, n)
      continue
    }
    i++
  }
  return { nc: nc.join(''), code: cd.join('') }
}

const lineOf = (text, idx) => {
  let line = 1
  for (let k = 0; k < idx && k < text.length; k++) if (text[k] === '\n') line++
  return line
}

/** `code[open] === '('` → { start, end } of the argument text (exclusive of the parens), or null if unbalanced. `code` is masked. */
function extractCall(code, open) {
  let depth = 0
  for (let i = open; i < code.length; i++) {
    if (code[i] === '(') depth++
    else if (code[i] === ')') {
      depth--
      if (depth === 0) return { start: open + 1, end: i }
    }
  }
  return null
}

/** Splits code[start,end) at top-level commas (depth over () [] {}). Returns [{ s, e }] (blank segments dropped). */
function splitTopLevel(code, start, end) {
  const parts = []
  let depth = 0
  let from = start
  for (let i = start; i < end; i++) {
    const c = code[i]
    if (c === '(' || c === '[' || c === '{') depth++
    else if (c === ')' || c === ']' || c === '}') depth--
    else if (c === ',' && depth === 0) {
      parts.push({ s: from, e: i })
      from = i + 1
    }
  }
  parts.push({ s: from, e: end })
  return parts.filter((p) => code.slice(p.s, p.e).trim() !== '')
}

/** Lower-cased camelCase / snake_case words of every identifier in a masked code fragment. */
function identifierWords(fragment) {
  const out = []
  for (const m of fragment.matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)) {
    for (const part of m[0].split(/_+/)) {
      for (const w of part.match(/[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+/g) ?? []) out.push(w.toLowerCase())
    }
  }
  return out
}

const piiWordsIn = (fragment) => [...new Set(identifierWords(fragment).filter((w) => PII_WORDS.has(w)))]

/** Removes TelemetryEvents.X members and slot-wrapped spans from masked code (they are the sanctioned shapes). */
function stripSanctioned(code) {
  let s = code.replace(/TelemetryEvents\s*\.\s*\w+/g, ' ')
  const slot = /(?:PiiTransforms\s*\.\s*(?:FileName|IpAddress)|\bFileNameSlot|\bIpSlot)\s*\(/
  for (;;) {
    const m = slot.exec(s)
    if (!m) break
    const open = m.index + m[0].length - 1
    const call = extractCall(s, open)
    if (!call) break
    s = s.slice(0, m.index) + ' ' + s.slice(call.end + 1)
  }
  return s
}

const rel = (p) => path.relative(ROOT, p).split(path.sep).join('/')
const finding = (file, line, rule, message) => ({ file, line, rule, message })

// ---------------------------------------------------------------------------------------------------------------------
// Rules
// ---------------------------------------------------------------------------------------------------------------------

/** Server call sites (R1, R2, R4, R6) over one C# file. Pure: text in, findings out. */
function scanCs(file, text) {
  const { nc, code } = mask(text, 'cs')
  const findings = []
  let calls = 0
  for (const m of code.matchAll(/\bTelemetryClient\b|\.TrackEvent\s*\(|\.TrackTrace\s*\(/g)) {
    findings.push(finding(file, lineOf(text, m.index), 'R6', `bypass of the reporter ('${m[0].trim()}') — telemetry must go through WaTelemetryReporter (AC-056-4)`))
  }
  for (const m of code.matchAll(/\b_?telemetry\??\.(\w+)\s*\(/g)) {
    const method = m[1]
    const open = m.index + m[0].length - 1
    const line = lineOf(text, m.index)
    const call = extractCall(code, open)
    if (!call) {
      findings.push(finding(file, line, 'R0', `could not find the closing paren of ${method}(...) — scan is unreliable here`))
      continue
    }
    calls++
    const argText = nc.slice(call.start, call.end)
    const argCode = code.slice(call.start, call.end)
    if (EMAIL_RE.test(argText)) findings.push(finding(file, line, 'R1', `raw email pattern in ${method}(...) arguments (TA-9.4)`))
    if (STRING_SLOT_METHODS.has(method)) {
      const a = argText.trim()
      if (!(a.startsWith('PiiTransforms.') || a.includes('.FileNameSlot(') || a.includes('.IpSlot('))) {
        findings.push(finding(file, line, 'R4', `${method}(...) argument is not slot-wrapped — hash via PiiTransforms.* / FileNameSlot / IpSlot (TA-9.4)`))
      }
    }
    if (!SLOT_METHODS.has(method)) {
      const bad = piiWordsIn(stripSanctioned(argCode))
      if (bad.length > 0) findings.push(finding(file, line, 'R2', `${method}(...) argument names PII (${bad.join(', ')}) — hash via FileNameSlot/IpSlot (TA-9.4)`))
    }
  }
  return { findings, calls }
}

/** Reporter log templates (R1, R3) over WaTelemetryReporter.cs. */
function scanReporter(file, text) {
  const { nc, code } = mask(text, 'cs')
  const findings = []
  let templates = 0
  for (const m of code.matchAll(/\b_logger\s*\.\s*Log\w*\s*\(/g)) {
    const open = m.index + m[0].length - 1
    const call = extractCall(code, open)
    if (!call) continue
    const first = nc.slice(call.start, call.end).match(/^\s*"((?:[^"\\]|\\.)*)"/)
    if (!first) continue // LogInformation(eventName): no literal template
    templates++
    const line = lineOf(text, m.index)
    const template = first[1]
    if (EMAIL_RE.test(template)) findings.push(finding(file, line, 'R1', 'raw email pattern in a reporter log template'))
    for (const ph of template.matchAll(/\{(\w+)(?::[^}]*)?\}/g)) {
      if (SERVER_PROPS.has(ph[1])) continue
      const pii = piiWordsIn(ph[1]).length > 0
      findings.push(
        finding(file, line, 'R3', `template placeholder {${ph[1]}} is not in SERVER_PROPS${pii ? ' — PII — hash via FileNameSlot/IpSlot (TA-9.4)' : ' — add it to the allowlist in check-telemetry-pii.mjs if it is not PII'}`),
      )
    }
  }
  return { findings, templates }
}

/** Collects the keys (and value fragments) of an object-literal body code[start,end). Returns { keys, values, opaque }. */
function objectKeys(code, start, end) {
  const keys = []
  const values = []
  const opaque = []
  for (const seg of splitTopLevel(code, start, end)) {
    const t = code.slice(seg.s, seg.e).trim()
    let m
    if (t.startsWith('...')) {
      const rest = t.slice(3)
      const base = seg.s + code.slice(seg.s, seg.e).indexOf('...') + 3
      let found = false
      for (let i = 0; i < rest.length; i++) {
        if (rest[i] !== '{') continue
        let depth = 0
        let j = i
        for (; j < rest.length; j++) {
          if (rest[j] === '{') depth++
          else if (rest[j] === '}' && --depth === 0) break
        }
        const inner = objectKeys(code, base + i + 1, base + j)
        keys.push(...inner.keys)
        values.push(...inner.values)
        opaque.push(...inner.opaque)
        found = true
        i = j
      }
      if (!found) opaque.push(t)
    } else if ((m = /^(\w+)\s*:/.exec(t))) {
      keys.push(m[1])
      values.push(t.slice(m[0].length))
    } else if (/^\w+$/.test(t)) {
      keys.push(t)
      values.push(t)
    } else {
      opaque.push(t)
    }
  }
  return { keys, values, opaque }
}

/** Client call sites (R1, R5, R6) over one ts/tsx file. `opts.definition` = the clientTelemetry.ts definition file. */
function scanTs(file, text, opts = {}) {
  const { nc, code } = mask(text, 'ts')
  const findings = []
  let calls = 0
  if (!opts.definition && nc.includes(INGESTION_HOST)) {
    findings.push(finding(file, lineOf(text, nc.indexOf(INGESTION_HOST)), 'R6', `App Insights ingestion host outside ${CLIENT_TELEMETRY_TS} — telemetry must go through trackClientEvent (AC-056-4)`))
  }
  if (opts.definition) return { findings, calls }
  for (const m of code.matchAll(/\btrackClientEvent\s*\(/g)) {
    const open = m.index + m[0].length - 1
    const line = lineOf(text, m.index)
    const call = extractCall(code, open)
    if (!call) {
      findings.push(finding(file, line, 'R0', 'could not find the closing paren of trackClientEvent(...) — scan is unreliable here'))
      continue
    }
    calls++
    if (EMAIL_RE.test(nc.slice(call.start, call.end))) findings.push(finding(file, line, 'R1', 'raw email pattern in trackClientEvent(...) arguments (TA-9.4)'))
    const args = splitTopLevel(code, call.start, call.end)
    if (args.length < 2) continue // no props
    const propsText = code.slice(args[1].s, args[1].e).trim()
    if (!propsText.startsWith('{')) {
      findings.push(finding(file, line, 'R5', 'props must be an object literal so the guard can see the keys (got a non-literal expression)'))
      continue
    }
    const bodyStart = args[1].s + code.slice(args[1].s, args[1].e).indexOf('{') + 1
    const bodyEnd = args[1].s + code.slice(args[1].s, args[1].e).lastIndexOf('}')
    const { keys, values, opaque } = objectKeys(code, bodyStart, bodyEnd)
    for (const k of keys) {
      if (!CLIENT_PROPS.has(k)) findings.push(finding(file, line, 'R5', `prop key '${k}' is not in CLIENT_PROPS${piiWordsIn(k).length ? ' — PII — never send it (TA-9.4)' : ' — add it to the allowlist in check-telemetry-pii.mjs if it is not PII'}`))
    }
    for (const o of opaque) findings.push(finding(file, line, 'R5', `cannot see the keys of '${o.replace(/\s+/g, ' ').slice(0, 40)}' — spread a literal object, not a variable`))
    for (const v of values) {
      const bad = piiWordsIn(v)
      if (bad.length > 0) findings.push(finding(file, line, 'R5', `prop value expression names PII (${bad.join(', ')}) (TA-9.4)`))
    }
  }
  return { findings, calls }
}

// ---------------------------------------------------------------------------------------------------------------------
// Tree walk + main
// ---------------------------------------------------------------------------------------------------------------------

const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', 'dist', '__lint-fixtures__', '.vs', '.git'])

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    const full = path.join(dir, name)
    const st = statSync(full)
    if (st.isDirectory()) {
      if (!SKIP_DIRS.has(name)) yield* walk(full)
    } else yield full
  }
}

function scanTree() {
  const findings = []
  const counts = { csFiles: 0, tsFiles: 0, serverCalls: 0, clientCalls: 0, templates: 0 }
  for (const full of walk(SRC)) {
    const r = rel(full)
    const isCs = full.endsWith('.cs')
    const isTs = /\.tsx?$/.test(full) && !/\.test\.tsx?$/.test(full) && r.startsWith('src/wa.web/src/')
    if (!isCs && !isTs) continue
    const text = readFileSync(full, 'utf8')
    if (isCs) {
      counts.csFiles++
      if (r === REPORTER_CS) {
        const res = scanReporter(r, text)
        findings.push(...res.findings)
        counts.templates += res.templates
        // The definition file is still checked for R6 bypass patterns (never for call-site rules R2/R4).
        findings.push(...scanCs(r, text).findings.filter((f) => f.rule === 'R6'))
      } else {
        const res = scanCs(r, text)
        findings.push(...res.findings)
        counts.serverCalls += res.calls
      }
    } else {
      counts.tsFiles++
      const res = scanTs(r, text, { definition: r === CLIENT_TELEMETRY_TS })
      findings.push(...res.findings)
      counts.clientCalls += res.calls
    }
  }
  return { findings, counts }
}

function runScan() {
  console.log('Telemetry PII guard (T-012-05, EC-012-3 / AC-012-3):')
  const { findings, counts } = scanTree()
  console.log(`  scanned ${counts.csFiles} C# files, ${counts.tsFiles} web files: ${counts.serverCalls} server helper calls, ${counts.templates} reporter templates, ${counts.clientCalls} client trackClientEvent calls`)
  findings.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line)
  for (const f of findings) console.error(`  ${f.file}:${f.line}  ${f.rule}  ${f.message}`)
  console.log('  rule  findings')
  for (const rule of ['R0', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6']) {
    const n = findings.filter((f) => f.rule === rule).length
    if (rule !== 'R0' || n > 0) console.log(`  ${rule}    ${n}`)
  }
  if (counts.serverCalls === 0 || counts.templates === 0 || counts.clientCalls === 0) {
    console.error('\nFAIL — scan found no call sites on a surface (path or pattern drift): the guard would pass vacuously.')
    process.exit(1)
  }
  if (findings.length > 0) {
    console.error(`\nFAIL — ${findings.length} telemetry PII finding(s). Route PII through FileNameSlot/IpSlot (TA-9.4) or, for a new non-PII property, extend the allowlist in scripts/check-telemetry-pii.mjs.`)
    process.exit(1)
  }
  console.log('\nPASS — 0 findings: no raw email, unhashed PII or unknown property reaches a telemetry call.')
}

// ---------------------------------------------------------------------------------------------------------------------
// Self-test: seeded violations must be flagged, today's real shapes must pass (D-4)
// ---------------------------------------------------------------------------------------------------------------------

function runSelfTest() {
  const scan = { cs: (c) => scanCs('selftest.cs', c).findings, reporter: (c) => scanReporter('selftest.cs', c).findings, ts: (c) => scanTs('selftest.ts', c).findings }
  const seeded = [
    ['S1', 'cs', 'R1', '_telemetry.TransferPageViewed(PiiTransforms.FileName("dev@example.com"));'],
    ['S2', 'cs', 'R2', '_telemetry.StorageBytesActive(user.Email.Length);'],
    ['S3', 'cs', 'R4', '_telemetry.TransferPageViewed(request.LinkId);'],
    ['S4', 'reporter', 'R3', 'public void X(string fileName) =>\n    _logger.LogInformation("upload_started {fileName}", fileName);'],
    ['S5', 'ts', 'R5', "trackClientEvent('upload_failed', { fileId: id, email: user.email })"],
    ['S6', 'ts', 'R5', "trackClientEvent('upload_failed', { reason: senderName })"],
    ['S7', 'ts', 'R1', "trackClientEvent('upload_failed', { reason: 'a@b.co' })"],
    ['S8', 'ts', 'R5', "trackClientEvent('upload_failed', props)"],
    ['S9', 'cs', 'R6', 'new TelemetryClient().TrackEvent("x");'],
  ]
  const clean = [
    ['C1', 'ts', "if (x) {\n  trackClientEvent('upload_failed', { fileId: id, retries: fileAttempts.get(id)?.retries, ...(isBlobUnavailable ? { reason: 'blob_unavailable' } : {}) })\n}"],
    ['C2', 'cs', '_telemetry.TransferPageViewed(PiiTransforms.FileName(request.LinkId));'],
    ['C3', 'cs', '_telemetry?.LogEvent(TelemetryEvents.EmailTemplateFallback);'],
    ['C4', 'ts', "trackClientEvent('download_completed', {\n  fileId: null, // TA-7.2: fileId=null for download-all.\n  bytes: totalBytes,\n  durationMs: Math.round(performance.now() - startedAt),\n})"],
    ['C5', 'cs', '_telemetry.UploadCompleted(\n    totalBytes,\n    stopwatch.ElapsedMilliseconds,\n    totalRetries);'],
  ]
  let caught = 0
  let passed = 0
  const failures = []
  for (const [id, kind, rule, code] of seeded) {
    const got = scan[kind](code)
    if (got.some((f) => f.rule === rule)) caught++
    else failures.push(`${id}: expected ${rule}, got [${got.map((f) => f.rule).join(', ') || 'nothing'}]`)
  }
  for (const [id, kind, code] of clean) {
    const got = scan[kind](code)
    if (got.length === 0) passed++
    else failures.push(`${id}: expected clean, got ${got.map((f) => `${f.rule} ${f.message}`).join('; ')}`)
  }
  console.log(`self-test: ${caught}/${seeded.length} seeded violations caught, ${passed}/${clean.length} clean shapes passed`)
  if (failures.length > 0) {
    for (const f of failures) console.error(`  - ${f}`)
    console.error('\nFAIL — self-test expectations not met.')
    process.exit(1)
  }
  console.log('PASS — every seeded violation is flagged and every clean shape passes.')
}

const mode = process.argv[2]
if (mode === undefined) runScan()
else if (mode === 'selftest') runSelfTest()
else {
  console.error(`unknown argument '${mode}' — usage: node scripts/check-telemetry-pii.mjs [selftest]`)
  process.exit(2)
}
