/**
 * Azurite-only blob SAS re-sign (CHORE-staging-blockers; TA-3.6 SAS strategy, TA-14.3 / T-055-05 local-emulator convention).
 *
 * The local Azurite account is IP-hosted: the minted URL's authority drops the `/devstoreaccount1` path segment (a raw
 * GET 404s) and its `spr=https` signature rejects plain HTTP. For Azurite URLs only, the browser re-addresses the URL to
 * the canonical `/devstoreaccount1{path}` form and re-signs it with Microsoft's public, well-known emulator key, with the
 * protocol line changed to `https,http`. Every other URL (real Azure Blob, `*.blob.core.windows.net`) is returned
 * byte-for-byte unchanged: the server-minted SAS is already correct there and a re-sign would only break it (403).
 */

/** The fixed loopback origin recipient downloads are re-addressed to against local Azurite. */
export const AZURITE_LOOPBACK_ORIGIN = 'http://127.0.0.1:10000'

const AZURITE_ACCOUNT_NAME = 'devstoreaccount1'
// Microsoft's published emulator key — not a secret; it is only ever applied to URLs that pass `isAzuriteBlobUrl`.
const AZURITE_ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
const AZURITE_BLOB_PORT = '10000'
const AZURITE_HOSTNAMES = new Set(['127.0.0.1', 'localhost', 'host.docker.internal', '[::1]', '::1'])

const toBase64 = (buf: ArrayBuffer) => btoa(String.fromCharCode(...new Uint8Array(buf)))

/**
 * True when the URL points at the local Azurite emulator: a loopback/docker host on the Azurite blob port (10000), or a
 * path that starts with the `/devstoreaccount1/` account segment. An unparsable URL is not Azurite (the caller uses it verbatim).
 */
export function isAzuriteBlobUrl(url: string): boolean {
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return false
  }
  if (parsed.pathname.startsWith(`/${AZURITE_ACCOUNT_NAME}/`)) return true
  return AZURITE_HOSTNAMES.has(parsed.hostname) && parsed.port === AZURITE_BLOB_PORT
}

/**
 * Returns the URL the browser should use for a minted blob SAS. Non-Azurite URLs come back as the very same string
 * (no `URL` round-trip). Azurite URLs are re-addressed to `{origin}/devstoreaccount1{path}` and re-signed over the
 * 16-line blob-SAS string-to-sign with the protocol line `https,http` (sp, st, se, canonical resource, identifier, iprange,
 * protocol, sv, sr, snapshot, encryption scope, cache-control, content-disposition (the signed rscd), encoding, language,
 * content-type). The signed values come from the minted query (`sp` default `r`, `sv` default `2024`, `sr` default `b`).
 *
 * @param mintedUrl SAS URL minted by the server.
 * @param origin Origin to address an Azurite URL to; defaults to the minted URL's own origin.
 */
export async function toBrowserBlobUrl(mintedUrl: string, origin?: string): Promise<string> {
  if (!isAzuriteBlobUrl(mintedUrl)) return mintedUrl

  const minted = new URL(mintedUrl)
  const q = minted.searchParams
  const sv = q.get('sv') ?? '2024'
  const st = q.get('st') ?? ''
  const se = q.get('se') ?? ''
  const sp = q.get('sp') ?? 'r'
  const sr = q.get('sr') ?? 'b'
  const rscd = q.get('rscd') ?? ''

  const key = await crypto.subtle.importKey(
    'raw',
    Uint8Array.from(atob(AZURITE_ACCOUNT_KEY_B64), (c) => c.charCodeAt(0)),
    { name: 'HMAC', hash: 'SHA-256' },
    true,
    ['sign'],
  )
  const lines = [
    sp, st, se, `/blob/${AZURITE_ACCOUNT_NAME}${minted.pathname}`, '', '', 'https,http', sv, sr, '', '', '', rscd, '', '', '',
  ]
  const sig = toBase64(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(lines.join('\n'))))

  const rscdPart = rscd === '' ? '' : `&rscd=${encodeURIComponent(rscd)}`
  return `${origin ?? minted.origin}/${AZURITE_ACCOUNT_NAME}${minted.pathname}?sv=${sv}&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=${sp}${rscdPart}&sig=${encodeURIComponent(sig)}`
}
