import i18n from '../i18n/init'

/** Human byte formatting — 1024-based, English unit names (TA-8.5). */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB'] as const
  let value = bytes
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }
  if (unitIndex === 0) return `${bytes} B`
  // EC-014-2: localize the NUMBER via Intl while the unit stays English in all locales.
  // min == max fraction digits preserves the original tiered precision + trailing zeros
  // ('1.50 KB', '5.00 GB' — formatBytes.test.ts asserts exact en strings).
  const digits = value < 10 ? 2 : 1
  const number = new Intl.NumberFormat(i18n.language, { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value)
  return `${number} ${units[unitIndex]}`
}

/** Compact byte formatting — 1024-based, no trailing zeros (TA-8.5). */
export function formatBytesShort(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB'] as const
  let value = bytes
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }
  if (unitIndex === 0) return `${bytes} B`
  // No forced trailing zeros: maximumFractionDigits only ('1 KB', '1.5 GB' — formatBytesShort.test.ts).
  const number = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 }).format(value)
  return `${number} ${units[unitIndex]}`
}
