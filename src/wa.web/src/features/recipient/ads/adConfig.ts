// US-024-01 (D3): ad provider ids. Fail closed: absent or malformed ids -> null -> the placement renders nothing.
// E1 (decided): build-time Vite client vars VITE_WA_ADSENSE_CLIENT / VITE_WA_ADSENSE_SLOT (public values; precedent VITE_WA_AI_KEY).
export interface AdConfig {
  client: string
  unit: string
}

const CLIENT_RE = /^ca-pub-\d{10,20}$/
const UNIT_RE = /^\d{6,20}$/

export function parseAdConfig(client?: string, unit?: string): AdConfig | null {
  const c = client?.trim()
  const u = unit?.trim()
  if (!c || !u || !CLIENT_RE.test(c) || !UNIT_RE.test(u)) return null
  return { client: c, unit: u }
}

// undefined = no override (read the build-time env); null/object = forced value (tests).
let override: AdConfig | null | undefined
export function setAdConfigForTest(value: AdConfig | null | undefined): void {
  override = value
}

export function getAdConfig(): AdConfig | null {
  if (override !== undefined) return override
  const env = (import.meta as { env?: Record<string, string | undefined> }).env
  return parseAdConfig(env?.VITE_WA_ADSENSE_CLIENT, env?.VITE_WA_ADSENSE_SLOT)
}
