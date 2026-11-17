// T-014-03 (FR-014-2): intentional lint-rule fixture. Ignored by npm run lint (base ignores);
// exercised by missingKeyTelemetry.test.ts's sibling lintRule.test.ts via the ESLint Node API.
export function LintProbe() {
  return (
    <div>
      <h1>Hardcoded heading</h1>
      <button type="button">Click me now</button>
    </div>
  )
}
