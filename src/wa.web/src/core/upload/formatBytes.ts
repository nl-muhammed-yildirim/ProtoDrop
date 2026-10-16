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
  const digits = value < 10 ? 2 : 1
  return `${value.toFixed(digits)} ${units[unitIndex]}`
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
  const rounded = Math.round(value * 10) / 10
  const str = Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
  return `${str} ${units[unitIndex]}`
}
