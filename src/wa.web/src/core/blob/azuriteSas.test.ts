import { describe, expect, it } from 'vitest'
import { AZURITE_LOOPBACK_ORIGIN, isAzuriteBlobUrl, toBrowserBlobUrl } from './azuriteSas'

// CHORE-staging-blockers: Azurite-only SAS re-sign. The oracles below are verbatim copies of the pre-change functions
// (StagingList.reSignUploadUrl and RecipientStore.browserDownloadUrl) — the new helper must match them byte for byte on Azurite URLs.

const KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
const ACCOUNT = 'devstoreaccount1'
const toBase64 = (buf: ArrayBuffer) => btoa(String.fromCharCode(...new Uint8Array(buf)))
const importKey = () =>
  crypto.subtle.importKey('raw', Uint8Array.from(atob(KEY_B64), (c) => c.charCodeAt(0)), { name: 'HMAC', hash: 'SHA-256' }, true, ['sign'])

const oldReSignUploadUrl = async (uploadUrl: string): Promise<string> => {
  const key = await importKey()
  const u = new URL(uploadUrl)
  const st = u.searchParams.get('st') ?? ''
  const se = u.searchParams.get('se') ?? ''
  const lines = [
    'cwr', st, se, '/blob/' + ACCOUNT + u.pathname, '', '', 'https,http',
    '2024', 'b', '', '', '', '', '', '', '',
  ]
  const sig = toBase64(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(lines.join('\n'))))
  return `${u.origin}/${ACCOUNT}${u.pathname}?sv=2024&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=cwr&sig=${encodeURIComponent(sig)}`
}

const oldBrowserDownloadUrl = async (mintedUrl: string): Promise<string> => {
  const minted = new URL(mintedUrl)
  const q = minted.searchParams
  const sv = q.get('sv') ?? '2024'
  const st = q.get('st') ?? ''
  const se = q.get('se') ?? ''
  const sp = q.get('sp') ?? 'r'
  const sr = q.get('sr') ?? 'b'
  const rscd = q.get('rscd') ?? ''
  const key = await importKey()
  const lines = [
    sp, st, se, `/blob/${ACCOUNT}${minted.pathname}`, '', '', 'https,http', sv, sr, '', '', '', rscd, '', '', '',
  ]
  const sig = toBase64(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(lines.join('\n'))))
  return `http://127.0.0.1:10000/${ACCOUNT}${minted.pathname}?sv=${sv}&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=${sp}&rscd=${encodeURIComponent(rscd)}&sig=${encodeURIComponent(sig)}`
}

describe('isAzuriteBlobUrl', () => {
  it.each([
    'http://127.0.0.1:10000/staging/x',
    'http://localhost:10000/staging/x',
    'http://host.docker.internal:10000/staging/x',
    'http://[::1]:10000/staging/x',
    'http://127.0.0.1:9999/devstoreaccount1/x',
    'http://localhost/devstoreaccount1/x',
    'http://example.test:10000/devstoreaccount1/x',
  ])('is true for %s', (url) => {
    expect(isAzuriteBlobUrl(url)).toBe(true)
  })

  it.each([
    'https://acct.blob.core.windows.net/transfers/x?sig=a',
    'https://azurite.example.com/transfers/x',
    'http://127.0.0.1:8080/x',
    'not a url',
    'https://acct.blob.core.windows.net/devstoreaccount1/blob?sig=a',
    'http://example.test:9999/devstoreaccount1/x',
  ])('is false for %s', (url) => {
    expect(isAzuriteBlobUrl(url)).toBe(false)
  })
})

describe('toBrowserBlobUrl', () => {
  const PROD =
    'https://wastg01.blob.core.windows.net/transfers/abc/def?sv=2024&spr=https&se=2030-01-01T00%3A30%3A00Z&sr=b&sp=r&rscd=attachment%3Bfilename%2A%3DUTF-8%27%27a.txt&sig=Zm9v%2Bbar%3D'

  it('returns a production URL byte-for-byte unchanged', async () => {
    expect(await toBrowserBlobUrl(PROD)).toBe(PROD)
  })

  it('returns a production URL unchanged even when an origin is passed', async () => {
    expect(await toBrowserBlobUrl(PROD, AZURITE_LOOPBACK_ORIGIN)).toBe(PROD)
  })

  it('matches the old upload re-sign for an Azurite upload SAS', async () => {
    const minted = 'http://127.0.0.1:10000/staging/d/f?sv=2024&spr=https&st=2026-01-01T00%3A00%3A00Z&se=2026-01-01T02%3A00%3A00Z&sr=b&sp=cwr&sig=x'
    const result = await toBrowserBlobUrl(minted)
    expect(result).toBe(await oldReSignUploadUrl(minted))
    expect(result.startsWith('http://127.0.0.1:10000/devstoreaccount1/staging/d/f?')).toBe(true)
    expect(result).toContain('spr=https%2Chttp')
  })

  it('matches the old recipient re-sign for an Azurite read SAS, addressed to the loopback origin', async () => {
    const minted =
      'http://host.docker.internal:10000/transfers/t/f?sv=2024&spr=https&st=2026-01-01T00%3A00%3A00Z&se=2026-01-01T02%3A00%3A00Z&sr=b&sp=r&rscd=attachment%3Bfilename%2A%3DUTF-8%27%27a.txt&sig=x'
    const result = await toBrowserBlobUrl(minted, AZURITE_LOOPBACK_ORIGIN)
    expect(result).toBe(await oldBrowserDownloadUrl(minted))
    expect(result.startsWith('http://127.0.0.1:10000/devstoreaccount1/transfers/t/f?')).toBe(true)
  })
})
