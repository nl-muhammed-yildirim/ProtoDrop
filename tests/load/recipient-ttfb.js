// T-029 (TA-14.1 load bullet 2, TA-15): recipient page TTFB p95 < 500 ms through Front Door, tagged with the load zone
// (run once per zone — TA-14.1 "from 2 zones"; OSS k6 runs from one machine, so the zone is a label, see README).
import http from 'k6/http';
import { check } from 'k6';

import { API_BASE_URL, PUBLIC_BASE_URL, TARGETS, ZONE, env, summaryHandler } from './lib/config.js';
import { seedSmallTransfer } from './lib/api.js';

const DURATION = env('DURATION', '3m');

export const options = {
  scenarios: {
    page: { executor: 'constant-arrival-rate', rate: 1, timeUnit: '1s', duration: DURATION, preAllocatedVUs: 5, maxVUs: 20, exec: 'page' },
    // Public API probe through Front Door, paced at 20/min — under the WAF's 30/min/IP limit on public paths.
    probe: { executor: 'constant-arrival-rate', rate: 20, timeUnit: '1m', duration: DURATION, preAllocatedVUs: 2, maxVUs: 5, exec: 'probe' },
  },
  thresholds: {
    'http_req_waiting{kind:page}': [`p(95)<${TARGETS.ttfbP95Ms}`],
    'http_req_waiting{kind:public_api}': [`p(95)<${TARGETS.ttfbP95Ms}`],
    'http_req_failed{kind:page}': ['rate<0.01'],
    'http_req_failed{kind:public_api}': ['rate<0.01'],
    checks: ['rate>0.99'],
  },
  setupTimeout: '2m',
};

/** Seeds one guest link through the DIRECT API (one draft call), then every request below goes through Front Door. */
export function setup() {
  API_BASE_URL(); // fail fast when unset
  const seeded = seedSmallTransfer(`k6-ttfb-${Date.now()}.txt`, null);
  return { linkId: seeded.linkId };
}

export function page(data) {
  const res = http.get(`${PUBLIC_BASE_URL()}/t/${data.linkId}`, { tags: { kind: 'page', zone: ZONE } });
  check(res, { 'page 200': (r) => r.status === 200 });
}

export function probe(data) {
  const res = http.get(`${PUBLIC_BASE_URL()}/api/v1/public/transfers/${data.linkId}`, { tags: { kind: 'public_api', zone: ZONE } });
  check(res, { 'probe 200': (r) => r.status === 200 });
}

export const handleSummary = summaryHandler('recipient-ttfb');
