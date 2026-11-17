import js from '@eslint/js'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'src/features/errors/__lint-fixtures__/**'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, tseslint.configs.recommended],
  },
  {
    // T-014-03 (FR-014-2 / T-026 exit check): fail CI on raw hardcoded JSX text children in the P0
    // surfaces swept by this task. Selector matches JSXText nodes containing at least one letter —
    // whitespace/indentation-only text is skipped, {t('...')} expressions are not JSXText.
    // Residual limitation (flagged for Reviewer): attribute literals (placeholder="...", aria-label="...")
    // are NOT caught by this selector; they were swept manually and regressions rely on tests.
    // Admin console + unsubscribe are grandfathered until T-026's full pass (~49 pre-existing strings).
    files: [
      'src/features/landing/**',
      'src/features/sender/**',
      'src/features/recipient/**',
      'src/features/auth/**',
      'src/features/files/**',
      'src/features/profile/**',
      'src/features/errors/**',
      'src/core/TopBar.tsx',
      'src/App.tsx',
    ],
    rules: { 'no-restricted-syntax': ['error', "JSXElement > JSXText[value=/[A-Za-z]/]"] },
  },
)
