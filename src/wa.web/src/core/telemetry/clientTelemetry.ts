/**
 * T-012-02 (TA-10.2, F-TRF-012): formalized client-side telemetry sink.
 *
 * Closed event list — exactly the four names below. Any other name FAILS FAST:
 * - dev (import.meta.env.DEV): throws an Error
 * - prod (import.meta.env.PROD): dropped silently (never logged)
 *
 * Transport: when VITE_WA_AI_KEY is present, fire-and-forget POST to App Insights
 * TrackEvents ingestion with no-cors mode so network failure never throws into app code.
 * When absent, only console.debug (M0 convention).
 */

import { getTraceParent } from '../traceparent'

/** The closed list of client telemetry event names (TA-10.2 frozen). */
export const CLIENT_TELEMETRY_EVENTS = [
  'growth_cta_shown',
  'growth_cta_clicked',
  'upload_failed',
  'download_completed',
  'translation_missing',
] as const

export type ClientTelemetryEvent = (typeof CLIENT_TELEMETRY_EVENTS)[number]

/** Test-overridable mutable log (same pattern as UploadEngine's module-level records). */
export const clientTelemetryLog: Array<{ event: string; props: Record<string, unknown>; traceparent: string }> = []

// App Insights ingestion endpoint (TrackEvents).
const APP_INSIGHTS_INGESTION_URL = 'https://dc.services.visualstudio.com/ingestion/track/events?api-version=2022-04-15'

/** Read the AI key from import.meta.env (new client config surface — no existing env reads in web src today). */
function aiKey(): string | undefined {
  if (aiKeyOverride !== null) return aiKeyOverride
  return (import.meta as { env?: Record<string, string> }).env?.VITE_WA_AI_KEY
}

/** True in dev builds — the fail-fast throw only fires here. */
function isDev(): boolean {
  return Boolean((import.meta as { env?: { DEV?: boolean } }).env?.DEV)
}

// Test override (UploadEngine's setSleepFnForTests pattern): lets tests force the prod drop branch.
let devOverride: boolean | null = null
export function setIsDevForTest(value: boolean | null): void {
  devOverride = value
}

// Test override for the AI key — import.meta.env is statically replaced by Vite, so tests set this directly.
let aiKeyOverride: string | undefined | null = null
export function setAiKeyForTest(value: string | undefined | null): void {
  aiKeyOverride = value
}

/**
 * Track a client telemetry event. FAILS FAST on any name not in the closed list:
 * - dev: throws an Error
 * - prod: dropped silently (never logged)
 */
export function trackClientEvent(name: string, props: Record<string, unknown> = {}): void {
  // Validate against the closed list — fail fast.
  if (!(CLIENT_TELEMETRY_EVENTS as readonly string[]).includes(name)) {
    if (devOverride === null ? isDev() : devOverride) {
      throw new Error(`[clientTelemetry] Unknown event name: "${name}" — not in the closed list.`)
    }
    // prod: drop silently — never log.
    return
  }

  const traceparent = getTraceParent()
  const entry = { event: name, props, traceparent }

  // Push to the exported mutable log (test-overridable).
  clientTelemetryLog.push(entry)

  // M0 convention: console.debug a one-line summary.
  console.debug(`[clientTelemetry] ${name} (${traceparent})`)

  // Transport: when AI key present, fire-and-forget POST to App Insights.
  const key = aiKey()
  if (key) {
    void sendToAppInsights(name, props, traceparent, key).catch(() => {
      // Fire-and-forget — network failure never throws into app code.
    })
  }
}

/**
 * Send event to App Insights via fire-and-forget fetch POST.
 * Uses no-cors mode so network failure never throws into app code.
 */
async function sendToAppInsights(name: string, props: Record<string, unknown>, traceparent: string, key: string): Promise<void> {
  const payload = {
    name,
    data: {
      ver: 2,
      properties: {
        ...props,
        traceparent,
      },
    },
  }

  await fetch(APP_INSIGHTS_INGESTION_URL, {
    method: 'POST',
    mode: 'no-cors',
    headers: {
      ikey: key,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(payload),
  })
}
