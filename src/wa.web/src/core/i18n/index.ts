// T-014-01: barrel so main.tsx imports one path; init runs synchronously on import.
export { default } from './init'
export { resolveInitialLanguage, readAccountLocale, setAccountLocale, SUPPORTED_LOCALES } from './init'
export type { SupportedLocale } from './init'
