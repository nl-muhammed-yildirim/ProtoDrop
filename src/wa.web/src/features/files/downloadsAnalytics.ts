import { getTraceParent } from '../../core/traceparent'
import i18n from '../../core/i18n/init'

/**
 * US-023-01 (F-PRF-003): pure helpers + fetchers for the Downloads panel — aggregates only (AC-023-2),
 * no raw IP / UA ever reaches the client. Server stays the authority; every failure here fails closed.
 */

const API_BASE = 'http://localhost:8080/api/v1'
const PLAN_URL = `${API_BASE}/billing/plan`

export interface DailyPoint { date: string; count: number }
export interface CountryBucket { code: string; count: number }
export interface BrowserBucket { family: string; count: number }

export interface FileCountDto { fileId: string; name: string; count: number }

export interface TransferAnalyticsDto {
  total: number
  daily: DailyPoint[]
  countries: CountryBucket[]
  browsers: BrowserBucket[]
  files: FileCountDto[]
  /** null = absent (single-file transfer, no zip) */
  downloadAll: number | null
  topFiles: FileCountDto[]
}

export type AnalyticsResult =
  | { kind: 'ok'; data: TransferAnalyticsDto }
  | { kind: 'proRequired' }
  | { kind: 'error' }

function isCount(n: unknown): n is number {
  return typeof n === 'number' && Number.isFinite(n) && n >= 0
}

function asArray(v: unknown): unknown[] {
  return Array.isArray(v) ? v : []
}

function parseFileCounts(v: unknown): FileCountDto[] {
  const out: FileCountDto[] = []
  for (const e of asArray(v)) {
    const f = e as { fileId?: unknown; name?: unknown; count?: unknown }
    if (typeof f?.fileId === 'string' && typeof f.name === 'string' && isCount(f.count)) out.push({ fileId: f.fileId, name: f.name, count: f.count })
  }
  return out
}

/** Total-safe parse: junk input yields an empty result; malformed entries are dropped. */
export function parseAnalytics(raw: unknown): TransferAnalyticsDto {
  const o = (typeof raw === 'object' && raw !== null ? raw : {}) as Record<string, unknown>
  const daily: DailyPoint[] = []
  for (const e of asArray(o.daily)) {
    const d = e as { date?: unknown; count?: unknown }
    if (typeof d?.date === 'string' && isCount(d.count)) daily.push({ date: d.date, count: d.count })
  }
  const countries: CountryBucket[] = []
  for (const e of asArray(o.countries)) {
    const c = e as { code?: unknown; count?: unknown }
    if (typeof c?.code === 'string' && isCount(c.count)) countries.push({ code: c.code, count: c.count })
  }
  const browsers: BrowserBucket[] = []
  for (const e of asArray(o.browsers)) {
    const b = e as { family?: unknown; count?: unknown }
    if (typeof b?.family === 'string' && isCount(b.count)) browsers.push({ family: b.family, count: b.count })
  }
  return {
    total: isCount(o.total) ? o.total : 0,
    daily,
    countries,
    browsers,
    files: parseFileCounts(o.files),
    downloadAll: isCount(o.downloadAll) ? o.downloadAll : null,
    topFiles: parseFileCounts(o.topFiles),
  }
}

/**
 * AC-023-3 (client half): whether the signed-in user's plan includes analytics, from GET /billing/plan.
 * Allowed only on a literal `features.analytics === true`; any failure, 401 or network error fails closed.
 */
export async function loadAnalyticsAllowed(): Promise<boolean> {
  try {
    const res = await fetch(PLAN_URL, { credentials: 'include', headers: { traceparent: getTraceParent() } })
    if (!res.ok) return false
    const dto = (await res.json()) as { features?: { analytics?: unknown } }
    return dto.features?.analytics === true
  } catch {
    return false
  }
}

/** GET /transfers/{id}/analytics: 200 → data, 403 → proRequired (downgrade race, EC-023-6), anything else → error. */
export async function fetchTransferAnalytics(id: string): Promise<AnalyticsResult> {
  try {
    const res = await fetch(`${API_BASE}/transfers/${encodeURIComponent(id)}/analytics`, {
      credentials: 'include',
      headers: { traceparent: getTraceParent() },
    })
    if (res.status === 403) return { kind: 'proRequired' }
    if (!res.ok) return { kind: 'error' }
    return { kind: 'ok', data: parseAnalytics(await res.json()) }
  } catch {
    return { kind: 'error' }
  }
}

/** SVG polyline `points` for the daily line. Zero days → ''; one day → flat line; all-zero → baseline. */
export function polylinePoints(daily: DailyPoint[], width: number, height: number): string {
  if (daily.length === 0) return ''
  const max = Math.max(...daily.map((d) => d.count))
  const pad = 2
  const innerH = height - pad * 2
  const yOf = (count: number): number => (max <= 0 ? height - pad : height - pad - (count / max) * innerH)
  const fmt = (n: number): string => String(Math.round(n * 100) / 100)
  if (daily.length === 1) {
    const y = fmt(yOf(daily[0].count))
    return `0,${y} ${width},${y}`
  }
  const step = width / (daily.length - 1)
  return daily.map((d, i) => `${fmt(i * step)},${fmt(yOf(d.count))}`).join(' ')
}

/** The first (highest-count) bucket's key, or null for an empty list (server already orders them). */
export function topLabel<T extends { count: number }>(buckets: T[], key: (b: T) => string): string | null {
  return buckets.length > 0 ? key(buckets[0]) : null
}

/** "??" → localized "Unknown"; otherwise the region display name for the code, falling back to the code itself. */
export function countryLabel(code: string, lang: string = i18n.language): string {
  if (code === '??') return i18n.t('files.analytics.unknownCountry')
  try {
    return new Intl.DisplayNames([lang], { type: 'region' }).of(code) ?? code
  } catch {
    return code
  }
}

/** "Other" → localized; every other family is a proper noun and stays as sent. */
export function browserLabel(family: string): string {
  return family === 'Other' ? i18n.t('files.analytics.otherBrowser') : family
}
