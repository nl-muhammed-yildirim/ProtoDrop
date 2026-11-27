import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'

import { request as pwRequest, type APIRequestContext, type APIResponse } from '@playwright/test'

import { API_URL, AUTH_DIR, BASE_URL } from './env'

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
    await retryPause(res)
    res = await send()
  }
  return res
}

/** Waits out a 429's Retry-After (capped; 30 s when absent). */
async function retryPause(res: APIResponse): Promise<void> {
  const retryAfter = Number(res.headers()['retry-after'])
  const waitMs = Math.min(Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 30_000, MAX_RETRY_AFTER_MS)
  await new Promise((resolve) => setTimeout(resolve, waitMs))
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
    let res = await ctx.delete(`${API}/auth/me`, { headers: sessionHeaders(token) })
    if (res.status() === 429) {
      await retryPause(res)
      res = await ctx.delete(`${API}/auth/me`, { headers: sessionHeaders(token) })
    }
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
 * Draft-path limiter (second line of defence): Front Door's DraftPerIp rule (10/min) matches /api/v1/transfers/draft INCLUDING
 * finalize. At most DRAFT_REQUESTS_PER_MINUTE such requests per rolling minute for traffic that goes THROUGH Front Door. The
 * timestamps live in a gitignored file under e2e/.auth/, so a worker restart does not reset the window. An API seed counts only
 * when the API origin is the Front Door origin; with E2E_API_URL pointing at the direct API origin (bypasses Front Door) API seeds
 * are not counted — only UI-driven flows are (noteUiDraft).
 */
const DRAFT_REQUESTS_PER_MINUTE = 8
const DRAFT_TIMES_FILE = path.join(AUTH_DIR, 'draft-times.json')
const API_VIA_FRONT_DOOR = new URL(API_URL).origin === new URL(BASE_URL).origin

function readDraftTimes(now: number): number[] {
  try {
    const all = JSON.parse(fs.readFileSync(DRAFT_TIMES_FILE, 'utf8')) as number[]
    return all.filter((t) => now - t < 60_000)
  } catch {
    return []
  }
}

async function reserveDraftRequests(count: number): Promise<void> {
  for (;;) {
    const now = Date.now()
    const times = readDraftTimes(now)
    if (times.length + count <= DRAFT_REQUESTS_PER_MINUTE) {
      for (let i = 0; i < count; i++) times.push(now)
      fs.mkdirSync(AUTH_DIR, { recursive: true })
      fs.writeFileSync(DRAFT_TIMES_FILE, JSON.stringify(times), 'utf8')
      return
    }
    await new Promise((resolve) => setTimeout(resolve, Math.max(60_000 - (now - times[0]), 250) + 50))
  }
}

/** Call BEFORE a UI action that makes a draft + finalize round trip through Front Door (guest upload, My Files Re-send). */
export async function noteUiDraft(): Promise<void> {
  await reserveDraftRequests(2)
}

async function paceSeed(): Promise<void> {
  if (API_VIA_FRONT_DOOR) await reserveDraftRequests(2) // draft + finalize
}

/**
 * draft -> single-shot blob PUT -> finalize (Idempotency-Key) -> send with no recipients (link-only). Returns an ACTIVE transfer.
 * Paced by paceSeed so draft + finalize stay under the Front Door draft budget.
 */
export async function seedTransfer(opts: SeedOptions): Promise<Seeded> {
  await paceSeed()
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

interface FlagRow { key: string; value: unknown; planDefault?: unknown; overridden?: boolean }

/** Reads one flag row from GET /admin/flags (value, plan default, overridden marker; no literal limits). Throws when not an admin. */
export async function getFlagRow(adminToken: string, key: string): Promise<{ value: string; planDefault: string; overridden: boolean }> {
  return withApi(async (ctx) => {
    const res = await ctx.get(`${API}/admin/flags`, { headers: sessionHeaders(adminToken) })
    if (!res.ok()) throw await failure('GET /admin/flags', res)
    const row = ((await res.json()) as FlagRow[]).find((r) => r.key === key)
    if (!row) throw new Error(`flag ${key} not found in GET /admin/flags`)
    return { value: String(row.value), planDefault: String(row.planDefault ?? row.value), overridden: row.overridden === true }
  })
}

/** PUT /admin/flags/{key} with the raw value as the body (SuperAdmin only). */
export async function setFlag(adminToken: string, key: string, value: string): Promise<void> {
  await withApi(async (ctx) => {
    const res = await ctx.put(`${API}/admin/flags/${encodeURIComponent(key)}`, {
      data: value,
      headers: { ...sessionHeaders(adminToken), 'Content-Type': 'application/json' },
    })
    if (res.status() === 403) throw new FlagForbiddenError(`PUT /admin/flags/${key} -> 403 (role/gate drift: the admin session may write flags no more)`)
    if (!res.ok()) throw await failure(`PUT /admin/flags/${key}`, res)
  })
}

/** Thrown when the admin session may not PUT flags (403); the expiry spec turns it into a skip with this message. */
export class FlagForbiddenError extends Error {}

/**
 * Proves the SuperAdmin role through GET /auth/me (`role: "superadmin"`): an Operator can READ flags but not PUT them, so a flags
 * probe would pass for the wrong role. Returns the role, or null when absent / not signed in. Retries once on 429 (auth budget).
 */
export async function adminRole(token: string): Promise<string | null> {
  return withApi(async (ctx) => {
    let res = await ctx.get(`${API}/auth/me`, { headers: sessionHeaders(token) })
    if (res.status() === 429) {
      await retryPause(res)
      res = await ctx.get(`${API}/auth/me`, { headers: sessionHeaders(token) })
    }
    if (!res.ok()) return null
    return ((await res.json()) as { role?: string }).role ?? null
  })
}

/**
 * Sets `key` to `value`, runs `fn`, and ALWAYS restores the flag. The restore target is the value read on entry, unless the row was
 * already overridden at `value` (leftover of a crashed run): then it is repaired to the PLAN DEFAULT first, logged, and restored there.
 * If both `fn` and the restore throw, the original error is rethrown with the restore failure appended.
 */
export async function withFlagOverride<T>(adminToken: string, key: string, value: string, fn: () => Promise<T>): Promise<T> {
  const before = await getFlagRow(adminToken, key)
  const repaired = before.overridden && before.value === value
  const restoreTo = repaired ? before.planDefault : before.value
  if (repaired) {
    console.warn(`[e2e] ${key} was already overridden at ${value} (leftover of an earlier run) — repairing to plan default ${before.planDefault}`)
    await setFlag(adminToken, key, restoreTo)
  }
  await setFlag(adminToken, key, value)
  let result: T
  try {
    result = await fn()
  } catch (err) {
    try {
      await setFlag(adminToken, key, restoreTo)
    } catch (restoreErr) {
      throw new Error(`${err instanceof Error ? err.message : String(err)} (flag restore ALSO failed — ${key} left at ${value}; restore it in Admin → Flags: ${String(restoreErr)})`, { cause: err })
    }
    throw err
  }
  try {
    await setFlag(adminToken, key, restoreTo)
  } catch (restoreErr) {
    // Never a FlagForbiddenError here: a failed restore must FAIL the run (the override already changed staging state).
    throw new Error(`flag restore failed — ${key} left at ${value}; restore it in Admin → Flags (${String(restoreErr)})`, { cause: restoreErr })
  }
  return result
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

/** Teardown variant: a missing, unreadable or partial file yields null instead of throwing. */
export function readSessionOrNull(): Partial<E2eSession> | null {
  try {
    return JSON.parse(fs.readFileSync(SESSION_FILE, 'utf8')) as Partial<E2eSession>
  } catch {
    return null
  }
}
