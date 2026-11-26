#!/usr/bin/env node
/**
 * T-012-05 (F-TRF-012, US-012-01; EC-012-3 / AC-012-3; TA-9.4 PII policy, TA-10.2 telemetry names): telemetry PII guard.
 *
 * Scans the two telemetry exits for raw email patterns and un-hashed / unmarked PII:
 *   server  — WaTelemetryReporter calls in src/**\/*.cs, matched by the reporter's PUBLIC METHOD NAMES on ANY receiver
 *             (`_telemetry.X(..)`, `_reporter.X(..)`, `reporter?.X(..)`, a minimal-API lambda parameter ...). The method list is parsed
 *             from WaTelemetryReporter.cs at scan time, so a new reporter method is covered automatically. Chosen over receiver-name or
 *             declared-type matching because it cannot be dodged by renaming the injected field; the price is a possible false positive
 *             when an unrelated type has a same-named method (none on the tree today — if one appears, handle it narrowly here).
 *   client  — trackClientEvent(name, props) calls in src/wa.web/src/**\/*.{ts,tsx} (props are spread verbatim into App Insights)
 *
 * Rules (all gate-tier; any finding exits 1):
 *   R1  email literal      — an address-shaped string (x@y.zz) in a helper call's arguments or in a reporter log template.
 *   R2  PII identifier     — a server helper argument that names email / mail / address / name / ip / password / passwd /
 *                            secret / sas / sig / signature (after stripping string literals, TelemetryEvents.X members and
 *                            slot-wrapped spans PiiTransforms.FileName(..) / PiiTransforms.IpAddress(..) / FileNameSlot(..) / IpSlot(..)).
 *   R3  template property  — every {placeholder} in a WaTelemetryReporter log template must be in SERVER_PROPS (closed set).
 *   R4  string-slot wrap   — an argument to a STRING_SLOT_METHODS member (TransferPageViewed) must be exactly ONE slot-wrapped call
 *                            (PiiTransforms.FileName(..) / IpAddress(..) / <recv>.FileNameSlot(..) / IpSlot(..)); nothing may remain
 *                            after the wrapped call is removed (so `PiiTransforms.FileName(x) + request.LinkId` fails).
 *   R5  client props       — trackClientEvent's 2nd argument must be an object literal whose keys are all in CLIENT_PROPS
 *                            and whose value expressions name no PII word (a bare identifier / spread variable hides the keys, so it fails).
 *   R6  bypass             — `trackClientEvent as x` imports / `= trackClientEvent` aliases in web files (calls through an alias would escape R5);
 *                            TelemetryClient / .TrackEvent( / .TrackTrace( in src/**\/*.cs, or the App Insights ingestion host in a
 *                            web file other than core/telemetry/clientTelemetry.ts (AC-056-4: the reporter / trackClientEvent are the single exits).
 *   R7  logger call        — T-012-06: any ILogger call (`.Log[Trace|Debug|Information|Warning|Error|Critical](..)` on ANY receiver) in src/**\/*.cs
 *                            whose template / arguments carry PII: an email literal, a {placeholder} or argument identifier naming
 *                            email / mail / address / ip / password / passwd / secret / sas / sig / signature, or a file / sender / display /
 *                            user / recipient / owner name (bare `name` is NOT flagged — container / account / event names are config, not PII).
 *                            Sanctioned slot-wrapped spans (FileNameSlot / IpSlot / PiiTransforms.*) are stripped first. Log an id instead (TA-9.4).
 *                            CHORE-code-style-2: source-generated logging is covered too. Every `[LoggerMessage(Level = .., Message = "..")]` template gets the same
 *                            template checks (email literal, {placeholder} identifiers); the declaration must be `private static partial void Name(ILogger ..`
 *                            (anything else lets call sites escape the scan and is a finding); every parameter other than the logger / the exception is
 *                            checked by name; and every in-file call `Name(logger, ..)` gets the argument checks above on the arguments after the logger.
 *   R8  event payload      — T-069 (F-XCT-005, US-048-01, AC-048-1/2): every TA-5.3 event payload (`Payload = new { .. }`, or `Payload = <id>` where
 *                            `<id> = new { .. }` is bound earlier in the same file) is checked key by key. A key naming a file name / IP / user agent
 *                            (words `ip`, `ua`, or `file`+`name`) must carry exactly ONE slot-wrapped call (PiiTransforms.FileName / IpAddress /
 *                            UserAgent, FileNameSlot, IpSlot). A key in EVENT_PII_KEYS (the TA-5.3 members that carry an address by contract) may carry
 *                            any value. Any other key, and its value identifiers, must not name PII (same naming rule as R7). An email literal anywhere
 *                            is a finding, and so is a payload whose shape the scan cannot see (`Payload = Build(x)`).
 *
 * Allowlists are CLOSED sets: adding a telemetry property means editing one list below — that diff is the PR-visible review point (EC-012-3).
 *
 * Run (repo root):   node scripts/check-telemetry-pii.mjs            scan the tree; exit 0 = PASS, 1 = findings
 *                    node scripts/check-telemetry-pii.mjs selftest   prove every seeded violation is flagged and every clean shape passes
 *
 * Limitation: a paren/quote-aware text scan, not a Roslyn/TS parser. Server: a reporter method taken as a delegate / method group
 * (`var f = reporter.X; f(..)`) or invoked via reflection escapes R1/R2/R4 (R6 + the reflection pin in WaTelemetryReporterTests mitigate).
 * Client: re-exporting (`export { trackClientEvent as x }`), passing the function as a value (`wrap(trackClientEvent)`), or calling it via
 * a destructured / namespace alias other than the two shapes R6 flags escapes R5. R8 is T-069's event-payload scan (the [Pii] attribute and erasure-list convention tests are C#, in tests/wa.application.unit/Privacy).
 * R7 also flags `.Path`/`.PathBase`/`.QueryString`/`.Message` member arguments (T-012-07: raw request path / exception message in a neutral slot).
 * R7 is a text scan too: a PII value laundered through a neutrally named local (`var x = user.Email; _logger.LogX("{X}", x)`) escapes it.
 * [LoggerMessage] limits: the attribute must use the named `Message = "<literal>"` argument (positional / const messages are findings), and the
 * generated method must be private static partial so every call site sits in the same file as its declaration.
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
const CLIENT_PROPS = new Set(['fileId', 'retries', 'reason', 'bytes', 'durationMs', 'key', 'locale', 'source', 'slotId'])
/** Reporter methods whose string parameter lands in a property — the argument must be slot-wrapped (TA-9.4). */
const STRING_SLOT_METHODS = new Set(['TransferPageViewed'])
/** The slot transforms themselves — their argument IS the raw value being hashed. */
const SLOT_METHODS = new Set(['FileNameSlot', 'IpSlot'])
/** R8: the only payload keys (TA-5.3) allowed to carry a raw address; widening this set is a reviewed, PII-shaped change (docs/AGENT.md section 7). */
const EVENT_PII_KEYS = new Set(['emails', 'senderEmail', 'email', 'to', 'actor'])
/** R8: the `Payload =` initializer of EventEnvelope (not `==`). */
const PAYLOAD_RE = /\bPayload\s*=(?!=)/g
const PII_WORDS = new Set(['email', 'mail', 'address', 'name', 'ip', 'password', 'passwd', 'secret', 'sas', 'sig', 'signature'])
/**
 * Public instance method names of WaTelemetryReporter, parsed from its source (so new methods are covered automatically).
 * Cross-checked against STRING_SLOT_METHODS / SLOT_METHODS: a rename there fails loudly instead of silently disarming a rule.
 */
function loadReporterMethods() {
  let src
  try {
    src = readFileSync(path.join(ROOT, REPORTER_CS), 'utf8')
  } catch {
    console.error(`FAIL — cannot read ${REPORTER_CS}: the guard cannot derive the reporter method list.`)
    process.exit(1)
  }
  const names = new Set()
  for (const m of src.matchAll(/^[ \t]*public\s+(?!(?:sealed|static|abstract|partial|class|record|struct|interface|const|readonly)\b)[\w<>[\]?.,]+\s+(\w+)\s*[(<]/gm)) names.add(m[1])
  const missing = [...STRING_SLOT_METHODS, ...SLOT_METHODS].filter((n) => !names.has(n))
  if (names.size === 0 || missing.length > 0) {
    console.error(`FAIL — reporter method list is empty or diverges from STRING_SLOT_METHODS/SLOT_METHODS (missing: ${missing.join(', ') || 'all'}) — update ${REPORTER_CS} or this script.`)
    process.exit(1)
  }
  return names
}
const REPORTER_METHODS = loadReporterMethods()
const REPORTER_CALL_RE = new RegExp(`\\.\\s*(${[...REPORTER_METHODS].join('|')})\\s*\\(`, 'g')
const EMAIL_RE = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/
/** R7: ILogger call on any receiver (does not match reporter `LogEvent(` — the level group is followed directly by `(`). */
const LOG_CALL_RE = /\.\s*Log(?:Trace|Debug|Information|Warning|Error|Critical)?\s*\(/g
const LOG_PII_WORDS = new Set(['email', 'mail', 'address', 'ip', 'password', 'passwd', 'secret', 'sas', 'sig', 'signature'])
const NAME_QUALIFIERS = new Set(['file', 'sender', 'display', 'user', 'recipient', 'owner', 'original'])

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

/** `code[open] === '{'` → { start, end } of the brace body (exclusive of the braces), or null if unbalanced. `code` is masked. */
function extractBraces(code, open) {
  let depth = 0
  for (let i = open; i < code.length; i++) {
    if (code[i] === '{') depth++
    else if (code[i] === '}') {
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
  return stripSlots(s)
}

/** Removes slot-wrapped calls (with any receiver prefix) from masked code. */
function stripSlots(code) {
  let s = code
  const slot = /(?:[A-Za-z_]\w*\s*\??\.\s*)*(?:PiiTransforms\s*\.\s*(?:FileName|IpAddress|UserAgent)|\bFileNameSlot|\bIpSlot)\s*\(/
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
  for (const m of code.matchAll(REPORTER_CALL_RE)) {
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
      const stripped = stripSlots(argCode)
      if (stripped === argCode || stripped.trim() !== '') {
        findings.push(finding(file, line, 'R4', `${method}(...) argument is not exactly one slot-wrapped call — hash via PiiTransforms.* / FileNameSlot / IpSlot (TA-9.4)`))
      }
    }
    if (!SLOT_METHODS.has(method)) {
      const bad = piiWordsIn(stripSanctioned(argCode))
      if (bad.length > 0) findings.push(finding(file, line, 'R2', `${method}(...) argument names PII (${bad.join(', ')}) — hash via FileNameSlot/IpSlot (TA-9.4)`))
    }
  }
  return { findings, calls }
}

/** True when an identifier names PII for R7 (bare `name` is deliberately not PII — see header). */
function isLogPiiIdentifier(id) {
  const words = identifierWords(id)
  if (words.some((w) => LOG_PII_WORDS.has(w))) return true
  return words.includes('name') && words.some((w) => NAME_QUALIFIERS.has(w))
}

/** Members of an anonymous object body: `key = expr`, a projection `a.b.Key` (key = last member) or a bare identifier. */
function csAnonMembers(code, start, end) {
  const out = []
  for (const seg of splitTopLevel(code, start, end)) {
    const text = code.slice(seg.s, seg.e)
    const kv = /^\s*([A-Za-z_]\w*)\s*=(?!=)/.exec(text)
    if (kv) {
      out.push({ key: kv[1], s: seg.s + kv[0].length, e: seg.e })
      continue
    }
    const last = /([A-Za-z_]\w*)\s*$/.exec(text)
    out.push({ key: last ? last[1] : '', s: seg.s, e: seg.e })
  }
  return out
}

/** R8 — TA-5.3 event payload shapes (`Payload = ..`) over one C# file. Pure: text in, findings out. */
function scanPayloads(file, text) {
  const { nc, code } = mask(text, 'cs')
  const findings = []
  let payloads = 0
  for (const m of code.matchAll(PAYLOAD_RE)) {
    const line = lineOf(text, m.index)
    const after = m.index + m[0].length
    let open = -1
    const direct = /^\s*new\s*\{/.exec(code.slice(after))
    if (direct) open = after + direct[0].length - 1
    else {
      const id = /^\s*([A-Za-z_]\w*)\s*(?=[,;})]|$)/.exec(code.slice(after))
      if (id) {
        const bind = new RegExp(`\\b${id[1]}\\s*=\\s*new\\s*\\{`, 'g')
        let last = null
        for (const b of code.slice(0, m.index).matchAll(bind)) last = b
        if (last) open = last.index + last[0].length - 1
      }
    }
    const body = open >= 0 ? extractBraces(code, open) : null
    if (!body) {
      findings.push(finding(file, line, 'R8', 'event payload shape is hidden (not `new { .. }` or a local bound to `new { .. }` in this file) — the guard cannot check its keys (TA-9.4)'))
      continue
    }
    payloads++
    for (const mem of csAnonMembers(code, body.start, body.end)) {
      const valueCode = code.slice(mem.s, mem.e)
      const valueText = nc.slice(mem.s, mem.e)
      const mline = lineOf(text, mem.s)
      const words = identifierWords(mem.key)
      if (EMAIL_RE.test(valueText)) findings.push(finding(file, mline, 'R8', `payload key '${mem.key}' carries a raw email literal (TA-9.4)`))
      if (words.includes('ip') || words.includes('ua') || (words.includes('file') && words.includes('name'))) {
        const stripped = stripSlots(valueCode)
        if (stripped === valueCode || stripped.trim() !== '') {
          findings.push(finding(file, mline, 'R8', `payload key '${mem.key}' names a file name / IP / user agent — its value must be exactly one slot-wrapped call (PiiTransforms.* / FileNameSlot / IpSlot) (AC-048-1)`))
        }
        continue
      }
      if (EVENT_PII_KEYS.has(mem.key)) continue
      const ids = new Set()
      if (isLogPiiIdentifier(mem.key)) ids.add(mem.key)
      for (const im of stripSlots(valueCode).matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)) if (isLogPiiIdentifier(im[0])) ids.add(im[0])
      if (ids.size > 0) {
        findings.push(finding(file, mline, 'R8', `payload key '${mem.key}' names PII (${[...ids].join(', ')}) outside EVENT_PII_KEYS — hash it via PiiTransforms.* or use a TA-5.3 key (AC-048-2)`))
      }
    }
  }
  return { findings, payloads }
}

/** R7 argument checks shared by `.LogX(..)` calls and [LoggerMessage] call sites. argText = comments-blanked text, argCode = strings blanked too. */
function logArgReasons(argText, argCode) {
  const reasons = []
  if (EMAIL_RE.test(argText)) reasons.push('raw email literal')
  for (const ph of argText.matchAll(/\{(\w+)(?::[^}]*)?\}/g)) {
    if (isLogPiiIdentifier(ph[1])) reasons.push(`placeholder {${ph[1]}}`)
  }
  const stripped = stripSanctioned(argCode)
  const ids = new Set()
  for (const im of stripped.matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)) if (isLogPiiIdentifier(im[0])) ids.add(im[0])
  for (const id of ids) reasons.push(`argument ${id}`)
  if (/\.\s*Name\b/.test(stripped)) reasons.push('argument .Name member')
  const member = /\.\s*(Path|PathBase|QueryString|Message)\b/.exec(stripped)
  if (member) reasons.push(`argument .${member[1]} member (raw request path / exception message — log LogRoute.Of(..) / a code instead)`)
  return reasons
}

/**
 * R7 for source-generated logging (CHORE-code-style-2): `[LoggerMessage(Level = .., Message = "..")] private static partial void LogX(ILogger logger, ..);`
 * 1. the attribute needs a named `Message = "<literal>"`; its template gets the email / placeholder checks;
 * 2. the declaration must be `private static partial void Name(` (so every call site is in this file and scanned);
 *    every parameter that is not the logger / an Exception is checked by name;
 * 3. every in-file call `Name(logger, args..)` gets the shared argument checks on the args after the logger.
 * Pure: text in, findings + counts out.
 */
function scanLoggerMessage(file, text) {
  const { nc, code } = mask(text, 'cs')
  const findings = []
  const names = new Set()
  let messages = 0
  let calls = 0
  const r7 = (idx, why) => findings.push(finding(file, lineOf(text, idx), 'R7', why))
  for (const m of code.matchAll(/\[\s*LoggerMessage\s*\(/g)) {
    messages++
    const open = m.index + m[0].length - 1
    const attr = extractCall(code, open)
    if (!attr) {
      r7(m.index, 'could not find the closing paren of [LoggerMessage(..)] — scan is unreliable here')
      continue
    }
    const attrText = nc.slice(attr.start, attr.end)
    const lit = /\bMessage\s*=\s*"((?:[^"\\]|\\.)*)"/.exec(attrText)
    if (lit && attrText.slice(lit.index + lit[0].length).trimStart().startsWith('+')) {
      r7(m.index, 'unreadable [LoggerMessage] (concatenated Message template — use a single "<literal>" so the guard reads the whole template)')
    } else if (!lit) {
      r7(m.index, 'unreadable [LoggerMessage] (use the named Message = "<literal>" argument so the guard can read the template)')
    } else {
      const template = lit[1]
      if (EMAIL_RE.test(template)) r7(m.index, '[LoggerMessage] template carries a raw email literal (TA-9.4)')
      for (const ph of template.matchAll(/\{(\w+)(?::[^}]*)?\}/g)) {
        if (isLogPiiIdentifier(ph[1])) r7(m.index, `[LoggerMessage] template placeholder {${ph[1]}} names PII — log an id instead (TA-9.4)`)
      }
    }
    // The declaration: everything after the attribute's closing `]` up to the first `;`.
    const close = code.indexOf(']', attr.end)
    const semi = code.indexOf(';', attr.end)
    const declText = close < 0 || semi < 0 ? '' : code.slice(close + 1, semi + 1)
    const decl = /^\s*private\s+static\s+partial\s+void\s+(\w+)\s*\(/.exec(declText)
    if (!decl) {
      r7(m.index, '[LoggerMessage] method must be `private static partial void` (call sites outside the file escape the scan)')
      continue
    }
    names.add(decl[1])
    const paramsOpen = close + 1 + decl[0].length - 1
    const params = extractCall(code, paramsOpen)
    if (!params) continue
    for (const seg of splitTopLevel(code, params.start, params.end)) {
      const toks = code.slice(seg.s, seg.e).trim().split(/\s+/)
      const name = toks[toks.length - 1]
      const type = toks.slice(0, -1).join(' ')
      if (toks.length < 2 || /^ILogger\b/.test(type) || /\bException\b/.test(type)) continue
      if (isLogPiiIdentifier(name)) r7(m.index, `[LoggerMessage] parameter ${name} names PII — log an id instead (TA-9.4)`)
    }
  }
  for (const name of names) {
    for (const m of code.matchAll(new RegExp(`(?<![\\w.])${name}\\s*\\(`, 'g'))) {
      if (/\bvoid\s+$/.test(code.slice(0, m.index))) continue // the declaration itself
      const open = m.index + m[0].length - 1
      const call = extractCall(code, open)
      if (!call) {
        r7(m.index, `could not find the closing paren of ${name}(..) — scan is unreliable here`)
        continue
      }
      calls++
      const args = splitTopLevel(code, call.start, call.end)
      if (args.length < 2) continue // logger only
      const from = args[1].s
      const reasons = logArgReasons(nc.slice(from, call.end), code.slice(from, call.end))
      if (reasons.length > 0) r7(m.index, `${name}(..) call carries PII (${reasons.join('; ')}) — log an id instead; never a raw email/sender name/file name/IP (TA-9.4)`)
    }
  }
  return { findings, messages, calls }
}

/** R7: ILogger calls over one C# file (any receiver). Pure: text in, findings + call count out. */
function scanLog(file, text) {
  const { nc, code } = mask(text, 'cs')
  const findings = []
  let calls = 0
  for (const m of code.matchAll(LOG_CALL_RE)) {
    const open = m.index + m[0].length - 1
    const line = lineOf(text, m.index)
    const call = extractCall(code, open)
    if (!call) {
      findings.push(finding(file, line, 'R0', 'could not find the closing paren of a logger call — scan is unreliable here'))
      continue
    }
    calls++
    const argText = nc.slice(call.start, call.end)
    const argCode = code.slice(call.start, call.end)
    const reasons = logArgReasons(argText, argCode)
    if (reasons.length > 0) {
      findings.push(finding(file, line, 'R7', `logger call carries PII (${reasons.join('; ')}) — log an id instead; never a raw email/sender name/file name/IP (TA-9.4)`))
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
  for (const m of code.matchAll(/\btrackClientEvent\s+as\s+\w+|=\s*trackClientEvent\b(?!\s*\()/g)) {
    findings.push(finding(file, lineOf(text, m.index), 'R6', `alias of trackClientEvent ('${m[0].replace(/\s+/g, ' ')}') — calls through an alias escape R5; call trackClientEvent directly`))
  }
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
  const counts = { csFiles: 0, tsFiles: 0, serverCalls: 0, clientCalls: 0, templates: 0, logCalls: 0, loggerMessages: 0, loggerMessageCalls: 0, payloads: 0 }
  for (const full of walk(SRC)) {
    const r = rel(full)
    const isCs = full.endsWith('.cs')
    const isTs = /\.tsx?$/.test(full) && !/\.test\.tsx?$/.test(full) && r.startsWith('src/wa.web/src/')
    if (!isCs && !isTs) continue
    const text = readFileSync(full, 'utf8')
    if (isCs) {
      counts.csFiles++
      const lr = scanLog(r, text)
      findings.push(...lr.findings)
      counts.logCalls += lr.calls
      const lm = scanLoggerMessage(r, text)
      findings.push(...lm.findings)
      counts.loggerMessages += lm.messages
      counts.loggerMessageCalls += lm.calls
      if (r !== REPORTER_CS) {
        const pl = scanPayloads(r, text)
        findings.push(...pl.findings)
        counts.payloads += pl.payloads
      }
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
  console.log('Telemetry PII guard (T-012-05 / T-012-06, EC-012-3 / AC-012-3):')
  const { findings, counts } = scanTree()
  console.log(`  scanned ${counts.csFiles} C# files, ${counts.tsFiles} web files: ${counts.serverCalls} server helper calls, ${counts.templates} reporter templates, ${counts.clientCalls} client trackClientEvent calls, ${counts.logCalls} logger calls, ${counts.loggerMessages} [LoggerMessage] methods, ${counts.loggerMessageCalls} [LoggerMessage] call sites, ${counts.payloads} event payloads`)
  findings.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line)
  for (const f of findings) console.error(`  ${f.file}:${f.line}  ${f.rule}  ${f.message}`)
  console.log('  rule  findings')
  for (const rule of ['R0', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8']) {
    const n = findings.filter((f) => f.rule === rule).length
    if (rule !== 'R0' || n > 0) console.log(`  ${rule}    ${n}`)
  }
  if (counts.serverCalls === 0 || counts.templates === 0 || counts.clientCalls === 0 || counts.logCalls === 0 || counts.loggerMessages === 0 || counts.payloads === 0) {
    console.error('\nFAIL — scan found no call sites on a surface (path or pattern drift): the guard would pass vacuously.')
    process.exit(1)
  }
  if (findings.length > 0) {
    console.error(`\nFAIL — ${findings.length} telemetry PII finding(s). Route PII through FileNameSlot/IpSlot (TA-9.4) or, for a new non-PII property, extend the allowlist in scripts/check-telemetry-pii.mjs.`)
    process.exit(1)
  }
  console.log('\nPASS — 0 findings: no raw email, unhashed PII or unknown property reaches a telemetry call or an ILogger line.')
}

// ---------------------------------------------------------------------------------------------------------------------
// Self-test: seeded violations must be flagged, today's real shapes must pass (D-4)
// ---------------------------------------------------------------------------------------------------------------------

function runSelfTest() {
  const scan = { cs: (c) => scanCs('selftest.cs', c).findings, reporter: (c) => scanReporter('selftest.cs', c).findings, ts: (c) => scanTs('selftest.ts', c).findings, log: (c) => scanLog('selftest.cs', c).findings, logmsg: (c) => scanLoggerMessage('selftest.cs', c).findings, payload: (c) => scanPayloads('selftest.cs', c).findings }
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
    ['S10', 'cs', 'R4', '_reporter.TransferPageViewed(request.LinkId);'],
    ['S11', 'cs', 'R2', 'reporter.StorageBytesActive(owner.Email.Length)'],
    ['S12', 'cs', 'R4', '_telemetry.TransferPageViewed(PiiTransforms.FileName(x) + request.LinkId)'],
    ['S13', 'ts', 'R6', "import { trackClientEvent as track } from '../core/telemetry/clientTelemetry'"],
    ['S14', 'ts', 'R6', 'const track = trackClientEvent'],
    ['S15', 'log', 'R7', '_logger.LogInformation("User created for signup email {email}", email);'],
    ['S16', 'log', 'R7', '_logger.LogError(ex, "f-email: failed to send email to {Address} attempt {Attempt}", recipient.Address, attempt);'],
    ['S17', 'log', 'R7', 'logger.LogWarning($"sent to {user.Email}");'],
    ['S18', 'log', 'R7', '_log.LogInformation("hello dev@example.com");'],
    ['S19', 'log', 'R7', '_logger.LogInformation("{File}", upload.FileName);'],
    ['S20', 'log', 'R7', '_logger.LogDebug("client {Client}", context.Connection.RemoteIpAddress);'],
    ['S21', 'log', 'R7', '_logger.LogInformation("{Who}", transfer.SenderName);'],
    ['S22', 'log', 'R7', '_logger.LogInformation("{F}", file.Name);'],
    ['S23', 'log', 'R7', '_logger.LogError(ex, "Unhandled on {P}", context.Request.Path);'],
    ['S24', 'log', 'R7', '_logger.LogWarning("Business error {Code}: {Detail}", ex.Code, ex.Message);'],
    ['S25', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Warning, Message = "sent to {RecipientEmail}")]\nprivate static partial void LogSent(ILogger logger, string recipientEmail);'],
    ['S26', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Warning, Message = "sent {Count}")]\nprivate static partial void LogSent(ILogger logger, string recipientEmail);'],
    ['S27', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Information, Message = "sent {Count}")]\nprivate static partial void LogSent(ILogger logger, string count);\nvoid M() { LogSent(_logger, user.Email); }'],
    ['S28', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Warning, Message = "failed for dev@example.com")]\nprivate static partial void LogFailed(ILogger logger);'],
    ['S29', 'logmsg', 'R7', '[LoggerMessage(1, LogLevel.Warning, "failed {Id}")]\nprivate static partial void LogFailed(ILogger logger, Guid id);'],
    ['S30', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Warning, Message = "failed {Id}")]\npublic static partial void LogFailed(ILogger logger, Guid id);'],
    ['S31', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Error, Message = "Unhandled on {Route}")]\nprivate static partial void LogFailed(ILogger logger, Exception exception, string route);\nvoid M() { LogFailed(_logger, ex, context.Request.Path); }'],
    ['S32', 'logmsg', 'R7', '[LoggerMessage(Level = LogLevel.Warning, Message = "sent " + "{RecipientEmail}")]\nprivate static partial void LogSent(ILogger logger, string id);'],
    ['S33', 'payload', 'R8', 'Payload = new { transferId = t.Id, fileName = file.OriginalName }'],
    ['S34', 'payload', 'R8', 'Payload = new { transferId = t.Id, recipient = recipient.Address }'],
    ['S35', 'payload', 'R8', 'Payload = new { ipHash = request.ClientIp }'],
    ['S36', 'payload', 'R8', 'Payload = BuildPayload(x)'],
    ['S37', 'payload', 'R8', 'Payload = new { to = "dev@example.com" }'],
    ['S38', 'log', 'R7', '_logger.LogInformation("sent {Email:Pii}", email);'],
  ]
  const clean = [
    ['C1', 'ts', "if (x) {\n  trackClientEvent('upload_failed', { fileId: id, retries: fileAttempts.get(id)?.retries, ...(isBlobUnavailable ? { reason: 'blob_unavailable' } : {}) })\n}"],
    ['C2', 'cs', '_telemetry.TransferPageViewed(PiiTransforms.FileName(request.LinkId));'],
    ['C3', 'cs', '_telemetry?.LogEvent(TelemetryEvents.EmailTemplateFallback);'],
    ['C4', 'ts', "trackClientEvent('download_completed', {\n  fileId: null, // TA-7.2: fileId=null for download-all.\n  bytes: totalBytes,\n  durationMs: Math.round(performance.now() - startedAt),\n})"],
    ['C6', 'cs', '_reporter.TransferPageViewed(_reporter.FileNameSlot(name));'],
    ['C7', 'ts', "import { trackClientEvent } from '../core/telemetry/clientTelemetry'"],
    ['C8', 'log', '_logger.LogInformation("User created {UserId}", userId);'],
    ['C9', 'log', '_logger.LogWarning("Draft rejected: {Detail}", detail);'],
    ['C10', 'log', `_logger.LogInformation("Blob container '{C}' ensured", name);`],
    ['C11', 'log', '_logger.LogInformation("file {Hash}", _telemetry.FileNameSlot(file.Name));'],
    ['C12', 'log', '_telemetry.LogEvent(TelemetryEvents.UserCreated);'],
    ['C13', 'log', '_logger.LogWarning("blob delete failed for {BlobPath}", blob.BlobPath);'],
    ['C14', 'log', '_logger.LogError(ex, "Unhandled exception on {Route}", LogRoute.Of(context));'],
    ['C15', 'logmsg', '[LoggerMessage(Level = LogLevel.Information, Message = "transfer {TransferId} expired")]\nprivate static partial void LogExpired(ILogger logger, Guid transferId);\nvoid M() { LogExpired(_logger, transfer.Id); }'],
    ['C16', 'logmsg', '[LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Route} — mapped to 500 INTERNAL")]\nprivate static partial void LogUnhandled(ILogger logger, Exception exception, string route);\nvoid M() { LogUnhandled(_logger, unhandled, LogRoute.Of(context)); }'],
    ['C17', 'payload', 'Payload = new { transferId = context.TransferId, to = recipient.Address, attempt = attempt, locale }'],
    ['C18', 'payload', 'var payload = new { transferId = transfer.Id, emails = addresses, senderEmail = transfer.SenderEmail };\nvar e = new EventEnvelope { Payload = payload };'],
    ['C19', 'payload', 'Payload = new { to = email, attempt = maxDeliveryCount, reason = ex.GetType().Name, kind, step }'],
    ['C20', 'payload', 'Payload = new { transferId = t.Id, fileName = PiiTransforms.FileName(file.OriginalName) }'],
    ['C21', 'payload', 'Payload = new { actor = actorEmail, action, entityType, entityId, details }'],
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
