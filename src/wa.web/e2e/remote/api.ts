import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'

import { request as pwRequest, type APIRequestContext, type APIResponse } from '@playwright/test'

import { API_URL, AUTH_DIR } from './env'

/**
 * T-028 (D4-D6, D8): the one deep module for staging HTTP — auth with a 429 retry, public-API seeding (draft -> blob PUT ->
 * finalize -> send), flag overrides and the session file. Specs call these helpers and never build URLs or cookies themselves.
 * Failures throw with status + body so a red run explains itself.
 */

const API = '/api/v1'
const SESSION_FILE = path.join(AUTH_DIR, 'session.json')
const MAX_RETRY_AFTER_MS = 65_000

/** Azurite's published emulator key (not a secret) — only applied when the minted URL is plain http (local stack). */
const AZURITE_ACCOUNT_NAME = 'devstoreaccount1'
const AZURITE_ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='

/** Runs `fn` with a short-lived API request context (no shared cookie jar — cookies are sent explicitly, D5). */
async function withApi<T>(fn: (ctx: APIRequestContext) => Promise<T>): Promise<T> {
  const ctx = await pwRequest.newContext({ baseURL: API_URL })
  try {
    return await fn(ctx)
  } finally {
    await ctx.dispose()
  }
}

async function failure(what: string, res: APIResponse): Promise<Error> {
  return new Error(`${what} failed — ${res.status()} ${(await res.text()).slice(0, 500)}`)
}

/** `Cookie: wa.session=<token>` — the cookie is Secure; an explicit header behaves the same on https staging and http localhost. */
export function sessionHeaders(token: string | undefined | null): Record<string, string> {
  return token ? { Cookie: `wa.session=${token}` } : {}
}

/** POST to an /auth/* path. Retries ONCE on 429, honouring Retry-After (capped), because the auth budget is 5 req/min per IP. */
async function authPost(ctx: APIRequestContext, route: string, data: unknown): Promise<APIResponse> {
  const send = (): Promise<APIResponse> => ctx.post(`${API}${route}`, { data })
  let res = await send()
  if (res.status() === 429) {
    const retryAfter = Number(res.headers()['retry-after'])
    const waitMs = Math.min(Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 30_000, MAX_RETRY_AFTER_MS)
    await new Promise((resolve) => setTimeout(resolve, waitMs))
    res = await send()
  }
  return res
}

interface SessionBody { sessionToken: string; email: string; userId: string }

/** POST /auth/signup — creates the account (a known email is a password-verified sign-in). Returns the session token. */
export async function signup(email: string, password: string): Promise<string> {
  return withApi(async (ctx) => {
    const res = await authPost(ctx, '/auth/signup', { email, password })
    if (!res.ok()) throw await failure('signup', res)
    return ((await res.json()) as SessionBody).sessionToken
  })
}

/** Outcome of a login attempt that may legitimately be a wrong password (admin bootstrap). */
export type LoginResult = { ok: true; token: string } | { ok: false; status: number; code: string | undefined }

/** POST /auth/login without throwing on 401, so the caller can branch on WRONG_PASSWORD. */
export async function tryLogin(email: string, password: string): Promise<LoginResult> {
  return withApi(async (ctx) => {
    const res = await authPost(ctx, '/auth/login', { email, password })
    if (res.ok()) return { ok: true as const, token: ((await res.json()) as SessionBody).sessionToken }
    let code: string | undefined
    try { code = ((await res.json()) as { code?: string }).code } catch { /* non-JSON error body */ }
    return { ok: false as const, status: res.status(), code }
  })
}

/** DELETE /auth/me with the session cookie. 401/404 are tolerated (already gone). */
export async function deleteMe(token: string): Promise<void> {
  await withApi(async (ctx) => {
    const res = await ctx.delete(`${API}/auth/me`, { headers: sessionHeaders(token) })
    if (!res.ok() && res.status() !== 401 && res.status() !== 404) throw await failure('delete account', res)
  })
}

// ---- Seeding through the real public API (D6) ----

export interface SeedFile { name: string; content: string; contentType?: string }
export interface SeedOptions {
  files: SeedFile[]
  password?: string
  /** Owner session token — when set every call carries the cookie so the transfer is owned (My Files). */
  sessionToken?: string
}
export interface Seeded {
  /** Transfer id, 'n' format. */
  id: string
  /** 8-char link id — the /t/{linkId} route segment. */
  linkId: string
  files: Array<{ fileId: string; name: string; sizeBytes: number }>
}

/**
 * Local Azurite only (minted URL is plain http): re-address to `/devstoreaccount1{path}` and re-sign with the emulator key,
 * protocol line `https,http` — the same string-to-sign as src/core/blob/azuriteSas.ts. https (real Azure) URLs pass through untouched.
 */
function browserBlobUrl(mintedUrl: string): string {
  const minted = new URL(mintedUrl)
  if (minted.protocol !== 'http:') return mintedUrl
  const q = minted.searchParams
  const sv = q.get('sv') ?? '2024'
  const st = q.get('st') ?? ''
  const se = q.get('se') ?? ''
  const sp = q.get('sp') ?? 'r'
  const sr = q.get('sr') ?? 'b'
  const rscd = q.get('rscd') ?? ''
  const lines = [sp, st, se, `/blob/${AZURITE_ACCOUNT_NAME}${minted.pathname}`, '', '', 'https,http', sv, sr, '', '', '', rscd, '', '', '']
  const sig = crypto.createHmac('sha256', Buffer.from(AZURITE_ACCOUNT_KEY_B64, 'base64')).update(lines.join('\n')).digest('base64')
  const rscdPart = rscd === '' ? '' : `&rscd=${encodeURIComponent(rscd)}`
  return `${minted.origin}/${AZURITE_ACCOUNT_NAME}${minted.pathname}?sv=${sv}&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=${sp}${rscdPart}&sig=${encodeURIComponent(sig)}`
}

/**
 * draft -> single-shot blob PUT -> finalize (Idempotency-Key) -> send with no recipients (link-only). Returns an ACTIVE transfer.
 * About 4 API calls per seed — spaced by the specs to stay under the Front Door draft budget of 10/min.
 */
export async function seedTransfer(opts: SeedOptions): Promise<Seeded> {
  const headers = sessionHeaders(opts.sessionToken)
  const files = opts.files.map((f) => ({
    name: f.name,
    sizeBytes: Buffer.byteLength(f.content),
    contentType: f.contentType ?? 'text/plain',
  }))
  return withApi(async (ctx) => {
    const draftRes = await ctx.post(`${API}/transfers/draft`, { data: { files }, headers })
    if (draftRes.status() !== 201) throw await failure('draft', draftRes)
    const draft = (await draftRes.json()) as { draftId: string; files: Array<{ fileId: string; uploadUrl: string }> }

    for (let i = 0; i < opts.files.length; i++) {
      const put = await ctx.put(browserBlobUrl(draft.files[i].uploadUrl), {
        data: Buffer.from(opts.files[i].content),
        headers: { 'x-ms-blob-type': 'BlockBlob', 'x-ms-version': '2024-08-04', 'Content-Type': files[i].contentType },
      })
      if (put.status() !== 201) throw await failure(`blob PUT (${files[i].name})`, put)
    }

    const finRes = await ctx.post(`${API}/transfers/draft/${draft.draftId}/finalize`, {
      data: { files: files.map((f, i) => ({ ...f, fileId: draft.files[i].fileId })) },
      headers: { ...headers, 'Idempotency-Key': `e2e-${draft.draftId}` },
    })
    if (finRes.status() !== 201) throw await failure('finalize', finRes)
    const fin = (await finRes.json()) as { id: string; linkId: string }

    const sendRes = await ctx.post(`${API}/transfers/${fin.id}/send`, {
      data: { emails: [], senderName: 'E2E', ...(opts.password !== undefined ? { password: opts.password } : {}) },
      headers: { ...headers, 'Idempotency-Key': `e2e-send-${draft.draftId}` },
    })
    if (sendRes.status() !== 201) throw await failure('send', sendRes)

    return {
      id: fin.id,
      linkId: fin.linkId,
      files: files.map((f, i) => ({ fileId: draft.files[i].fileId, name: f.name, sizeBytes: f.sizeBytes })),
    }
  })
}

// ---- Public recipient endpoints ----

interface PublicBody { downloadsLeft?: number | null; code?: string }

/** GET /public/transfers/{linkId} — returns the raw status + parsed body (callers assert 200 / 404 / 410). */
export async function publicGet(linkId: string): Promise<{ status: number; body: PublicBody }> {
  return withApi(async (ctx) => {
    const res = await ctx.get(`${API}/public/transfers/${linkId}`)
    let body: PublicBody = {}
    try { body = (await res.json()) as PublicBody } catch { /* empty body */ }
    return { status: res.status(), body }
  })
}

/** GET .../files/{fileId}/download-url — mints (and COUNTS) one download; returns the HTTP status. */
export async function mintDownload(linkId: string, fileId: string): Promise<number> {
  return withApi(async (ctx) => (await ctx.get(`${API}/public/transfers/${linkId}/files/${fileId}/download-url`)).status())
}

// ---- Admin flags (D8) ----

/** Reads a flag's current value from GET /admin/flags (never hard-code a limit — golden rule 2). Throws when not an admin. */
export async function getFlag(adminToken: string, key: string): Promise<string> {
  return withApi(async (ctx) => {
    const res = await ctx.get(`${API}/admin/flags`, { headers: sessionHeaders(adminToken) })
    if (!res.ok()) throw await failure('GET /admin/flags', res)
    const rows = (await res.json()) as Array<{ key: string; value: unknown }>
    const row = rows.find((r) => r.key === key)
    if (!row) throw new Error(`flag ${key} not found in GET /admin/flags`)
    return String(row.value)
  })
}

/** PUT /admin/flags/{key} with the raw value as the body (SuperAdmin only). */
export async function setFlag(adminToken: string, key: string, value: string): Promise<void> {
  await withApi(async (ctx) => {
    const res = await ctx.put(`${API}/admin/flags/${encodeURIComponent(key)}`, {
      data: value,
      headers: { ...sessionHeaders(adminToken), 'Content-Type': 'application/json' },
    })
    if (!res.ok()) throw await failure(`PUT /admin/flags/${key}`, res)
  })
}

/** Probes whether the token reaches the admin API (200). Setup uses it to learn whether the e2e admin actually holds the role. */
export async function probeAdmin(token: string): Promise<{ ok: boolean; status: number }> {
  return withApi(async (ctx) => {
    const res = await ctx.get(`${API}/admin/flags`, { headers: sessionHeaders(token) })
    return { ok: res.ok(), status: res.status() }
  })
}

/** Sets `key` to `value`, runs `fn`, and ALWAYS restores the original value (try/finally) — a crashed runner can still leave it set (R8). */
export async function withFlagOverride<T>(adminToken: string, key: string, value: string, fn: () => Promise<T>): Promise<T> {
  const original = await getFlag(adminToken, key)
  await setFlag(adminToken, key, value)
  try {
    return await fn()
  } finally {
    await setFlag(adminToken, key, original)
  }
}

// ---- Session file (written by staging-setup, read by specs + teardown) ----

export interface E2eSession {
  ownerEmail: string
  ownerToken: string
  /** Admin session token, or null when no admin login exists or it lacks the admin role. */
  adminToken: string | null
  /** Why adminToken is null — shown as the skip reason by expiry-flag.spec. */
  adminSkipReason: string | null
}

export function writeSession(s: E2eSession): void {
  fs.mkdirSync(AUTH_DIR, { recursive: true })
  fs.writeFileSync(SESSION_FILE, JSON.stringify(s), 'utf8')
}

export function readSession(): E2eSession {
  return JSON.parse(fs.readFileSync(SESSION_FILE, 'utf8')) as E2eSession
}
