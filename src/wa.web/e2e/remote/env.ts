import crypto from 'node:crypto'
import fs from 'node:fs'
import { fileURLToPath } from 'node:url'

import { test } from '@playwright/test'

/**
 * T-028: environment resolution for the `@staging` suite (e2e/remote/*). The suite talks to a DEPLOYED URL over HTTP + UI only
 * (no SQL, no Azurite SDK), so everything it needs comes from env vars: E2E_BASE_URL (the SPA / Front Door host), E2E_API_URL
 * (optional; the same convention as e2e/seed-transfer.ts), E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD (optional; expiry-flag only).
 */

const baseUrl = process.env.E2E_BASE_URL?.trim().replace(/\/+$/, '') || undefined

/** True when the suite runs against a deployed (or explicitly named) URL; without it every remote test skips itself. */
export const REMOTE = baseUrl !== undefined

/** The SPA origin, as in playwright.config.ts. */
export const BASE_URL = baseUrl ?? 'http://localhost:5173'

/** True for a local stack (http://localhost|127.0.0.1) — the only case where blob SAS URLs are plain-HTTP Azurite URLs. */
export const IS_LOCAL_HOST = /^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(BASE_URL)

/**
 * API origin without `/api/v1`. Remote default = the same origin as BASE_URL (Front Door routes /api/* and /* on one host);
 * local default = the API's own port, matching E2E_API_URL in e2e/seed-transfer.ts.
 */
export const API_URL = (process.env.E2E_API_URL?.trim().replace(/\/+$/, '')) || (IS_LOCAL_HOST ? 'http://localhost:8080' : BASE_URL)

/** Unique per run (CI run id, else a timestamp) plus 4 random hex chars — keeps accounts and file names collision-free. */
export const RUN_ID = `${process.env.GITHUB_RUN_ID ?? Date.now()}-${crypto.randomBytes(2).toString('hex')}`

const adminEmail = process.env.E2E_ADMIN_EMAIL?.trim()
const adminPassword = process.env.E2E_ADMIN_PASSWORD

/** The synthetic admin login, or null when either secret is unset (expiry-flag.spec skips). */
export const ADMIN: { email: string; password: string } | null =
  adminEmail && adminPassword ? { email: adminEmail, password: adminPassword } : null

/** Seconds the API caches flags (Wa:Flags:TtlSeconds fallback is 30) — the expiry spec polls for this long plus a margin. */
export const FLAGS_TTL_SECONDS = Number(process.env.E2E_FLAGS_TTL_SECONDS ?? 30)

/** Session files (cookies, tokens) — gitignored, never uploaded as an artifact (D11). */
export const AUTH_DIR = fileURLToPath(new URL('../.auth/', import.meta.url))

/** Skips the calling test/describe unless E2E_BASE_URL is set. Call inside a test or a describe body. */
export function requireRemote(): void {
  test.skip(!REMOTE, 'E2E_BASE_URL not set — @staging suite runs only against a deployed URL')
}

/**
 * UI copy from the app's own bundles (D10) — specs never hard-code labels. Flat keys, e.g. `t('en')['landing.staging.send']`.
 * Read at call time via import.meta.url (ESM package), same pattern as e2e/global-setup.ts.
 */
export function locale(lng: 'en' | 'de'): Record<string, string> {
  const file = fileURLToPath(new URL(`../../src/core/i18n/locales/${lng}.json`, import.meta.url))
  return JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, string>
}
