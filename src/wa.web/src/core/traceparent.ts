/**
 * T-012-01 (TA-10.1, FR-012-1): browser-side W3C traceparent — one per page load.
 *
 * The SPA mints a single valid W3C traceparent on first use and reuses it for the
 * rest of the page's lifetime (no reload needed). A refresh mints a new one (new
 * SPA lifetime), which is what makes the DevTools/App Insights check meaningful:
 * same user action chain = same id.
 *
 * Format: 00-{32-hex trace-id}-{16-hex parent-id}-07 (version "00", sampled flag).
 * Dependency-free so vitest can unit-test it.
 */

const HEX_CHARS = '0123456789abcdef'

/** Mint a fresh 32-hex trace id (16 random bytes) from crypto.getRandomValues. */
function randomHex(byteCount: number): string {
  const bytes = new Uint8Array(byteCount)
  crypto.getRandomValues(bytes)
  let out = ''
  for (const b of bytes) out += HEX_CHARS[b >> 4] + HEX_CHARS[b & 0x0f]
  return out
}

/** Build a fresh valid W3C traceparent value. */
export function mintTraceParent(): string {
  const traceId = randomHex(16) // 32 hex chars
  const parentId = randomHex(8) // 16 hex chars
  return `00-${traceId}-${parentId}-07`
}

// Memoized per page load — minted once on first call, reused for the rest of the session.
let cached: string | null = null

/**
 * Return the page-load's traceparent (memoized). First call mints; subsequent calls
 * return the same value until the page reloads (new SPA lifetime).
 */
export function getTraceParent(): string {
  if (cached === null) cached = mintTraceParent()
  return cached
}

/** Reset the memoized value (test-only — simulates a fresh page load). */
export function resetTraceParentForTest(): void {
  cached = null
}
