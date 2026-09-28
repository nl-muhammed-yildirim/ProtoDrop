// TA-8.5 (docs/03-technical-architecture.md): bytes via formatBytes() helper,
// 1024-based ("GB").

const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

export function formatBytes(bytes: number): string {
  let value = Number.isFinite(bytes) && bytes >= 0 ? bytes : 0;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < UNITS.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }
  const text =
    value < 10 ? trimTrailingZero(value.toFixed(1)) : Math.round(value).toString();
  return `${text} ${UNITS[unitIndex]}`;
}

function trimTrailingZero(text: string): string {
  return text.endsWith('.0') ? text.slice(0, -2) : text;
}
