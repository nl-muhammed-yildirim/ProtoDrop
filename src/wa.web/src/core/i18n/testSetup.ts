/**
 * T-014-03 (FR-014-4): Shared test setup for i18n-dependent tests.
 * Imports the real i18n instance and ensures it's initialized to 'en' for deterministic tests.
 */
import i18n from './init'

/**
 * Ensure i18n is initialized to English for tests.
 * Call this at the top of test files that use t() or Intl formatters.
 */
export async function setupI18nForTests(): Promise<void> {
  if (i18n.language !== 'en') {
    await i18n.changeLanguage('en')
  }
}

export default i18n
