// T-014-03 (FR-014-2 / T-026 exit check): the no-restricted-syntax rule in eslint.config.js fails on
// raw hardcoded JSX text children in P0 surfaces, passes {t('...')} expressions, and leaves admin +
// unsubscribe grandfathered until T-026's full pass.
//
// Mechanism (probed 2026-11-10 against eslint 9.39.5 flat config): ESLint.lintText(code, { filePath })
// resolves the config from filePath's directory and applies the files-scoped rule to that file — so we
// anchor at a real in-scope P0 path (ErrorScreen.tsx) for "fires" cases and an out-of-scope admin path
// for the grandfathering case. The committed fixture under __lint-fixtures__/ is ignored by `npm run lint`
// (base ignores) but its exact content is read here so the test tracks the real fixture, not a copy.
import { describe, expect, it } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

import { ESLint } from 'eslint'

// In-scope P0 anchor (exists + matches eslint.config.js files list). Out-of-scope admin anchor.
const IN_SCOPE = path.join(process.cwd(), 'src', 'features', 'errors', 'ErrorScreen.tsx')
const OUT_OF_SCOPE = path.join(process.cwd(), 'src', 'features', 'admin', 'somefile.tsx')

// The committed fixture — read so this test fails if the fixture drifts from what it's meant to prove.
const FIXTURE = fs.readFileSync(
  path.join(process.cwd(), 'src', 'features', 'errors', '__lint-fixtures__', 'Probe.tsx'),
  'utf-8',
)

// A clean component: only {t('...')} expressions (no raw JSXText with letters).
const CLEAN_CODE = `export function Clean() {\n  return (\n    <div>\n      <h1>{t('a.b.c')}</h1>\n      <button type="button">{t('d.e.f', { count: 3 })}</button>\n    </div>\n  )\n}\n`

async function lintRules(code: string, filePath: string): Promise<string[]> {
  const eslint = new ESLint({ cwd: process.cwd() })
  const results = await eslint.lintText(code, { filePath })
  return (results[0].messages ?? []).map((m) => m.ruleId as string)
}

describe('T-014-03 no-hardcoded-JSX lint rule (FR-014-2)', () => {
  it('fires no-restricted-syntax on the fixture hardcoded JSX text children', async () => {
    const rules = await lintRules(FIXTURE, IN_SCOPE)
    // The fixture has exactly two raw hardcoded strings: "Hardcoded heading" + "Click me now".
    expect(rules.filter((r) => r === 'no-restricted-syntax')).toHaveLength(2)
  })

  it('passes a component that renders only {t(...)} expressions (no false positives)', async () => {
    const rules = await lintRules(CLEAN_CODE, IN_SCOPE)
    expect(rules).toEqual([])
  })

  it('grandfathers admin + unsubscribe: the same hardcoded JSX is clean outside the P0 scope', async () => {
    const rules = await lintRules(FIXTURE, OUT_OF_SCOPE)
    expect(rules.filter((r) => r === 'no-restricted-syntax')).toHaveLength(0)
  })
})
