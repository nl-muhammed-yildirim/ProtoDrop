/**
 * Generates an idempotency key as a SHA-256 hex string (TA-4.1.5).
 * The key is deterministic for the same input, ensuring retries use the same key.
 * Format: 64-character lowercase hex string with 24h TTL on the server side.
 */
export async function generateIdempotencyKey(input: string): Promise<string> {
  const encoder = new TextEncoder();
  const data = encoder.encode(input);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  return Array.from(new Uint8Array(hashBuffer))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
}
