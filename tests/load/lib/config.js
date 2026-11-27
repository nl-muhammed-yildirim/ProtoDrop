// T-029: shared k6 configuration — environment parsing and the TA-15 targets as named constants.
// k6 built-ins only (no jslib / npm imports), so the scripts run on a bare k6 binary.
import { fail } from 'k6';

/** Reads an env var (k6 exposes them via __ENV); returns `fallback` when unset or blank. */
export function env(name, fallback) {
  const v = __ENV[name];
  return v === undefined || v === '' ? fallback : v;
}

/** Reads a required env var; aborts the run immediately with a clear message when it is missing. */
export function required(name) {
  const v = env(name, undefined);
  if (v === undefined) fail(`${name} is required (see tests/load/README.md)`);
  return v;
}

/** The synthetic admin login (E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD) or null when either is unset. */
export function adminCredsOrNull() {
  const email = env('E2E_ADMIN_EMAIL', undefined);
  const password = env('E2E_ADMIN_PASSWORD', undefined);
  return email && password ? { email, password } : null;
}

const trimSlash = (u) => u.replace(/\/+$/, '');

/** Direct API origin (Container Apps FQDN) — used for load, so the Front Door WAF rate limits are not what gets measured. */
export const API_BASE_URL = () => trimSlash(required('API_BASE_URL'));

/** Public origin through Front Door — used for recipient TTFB, which is what users actually hit. */
export const PUBLIC_BASE_URL = () => trimSlash(required('PUBLIC_BASE_URL'));

/** Load-zone label (TA-14.1 "from 2 zones"): every request of a TTFB run is tagged with it. */
export const ZONE = env('ZONE', 'default');

/** TA-15 / TA-14.1 targets. Each is the number the matching k6 threshold is built from. */
export const TARGETS = {
  /** TA-15: API read p50 < 100 ms. */
  apiP50Ms: 100,
  /** TA-15: API read p99 < 500 ms. */
  apiP99Ms: 500,
  /** TA-15: recipient page TTFB p95 < 500 ms. */
  ttfbP95Ms: 500,
  /** TA-15: 5 GB upload in under 10 minutes on a 100 Mbps line. */
  uploadMaxMs: 600000,
  /** TA-14.1 email burst: API accept latency of a send (the inbox leg is measured after the run, see README). */
  burstAcceptP95Ms: 1000,
  /** TA-15: send -> inbox p95 < 60 s — evaluated with the KQL in the README, k6 cannot see an inbox. */
  emailP95Sec: 60,
};

/**
 * Builds the `handleSummary` every script exports: writes `summary.json` and prints one line per threshold (name, ok/FAILED).
 * `extraFiles(data)` may return more files (the email burst adds its manifest).
 */
export function summaryHandler(name, extraFiles) {
  return function handleSummary(data) {
    const lines = [`== ${name} summary ==`];
    for (const [metric, m] of Object.entries(data.metrics)) {
      if (!m.thresholds) continue;
      for (const [expr, res] of Object.entries(m.thresholds)) {
        lines.push(`${res.ok ? 'ok     ' : 'FAILED '} ${metric} ${expr}`);
      }
    }
    const out = { 'summary.json': JSON.stringify(data, null, 2), stdout: lines.join('\n') + '\n' };
    if (extraFiles) Object.assign(out, extraFiles(data));
    return out;
  };
}
