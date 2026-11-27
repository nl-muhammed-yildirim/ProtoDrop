// T-029 (TA-14.1 load bullet 1, TA-15): API read latency. Two constant-arrival-rate scenarios against the DIRECT API origin:
// public (GET /public/transfers/{linkId}) and authed (GET /transfers, GET /transfers/{id}). Never /auth/me (rate-limited).
import { check } from 'k6';

import { env, required, summaryHandler, TARGETS } from './lib/config.js';
import { get, seedSmallTransfer, signupIfNeeded } from './lib/api.js';

const RATE = Number(env('RATE', '20')); // requests/s per scenario
const DURATION = env('DURATION', '5m');

const scenario = (exec) => ({
  executor: 'constant-arrival-rate',
  rate: RATE,
  timeUnit: '1s',
  duration: DURATION,
  preAllocatedVUs: 50,
  maxVUs: 200,
  exec,
});

export const options = {
  scenarios: { public: scenario('publicRead'), authed: scenario('authedRead') },
  thresholds: {
    'http_req_duration{kind:api}': [`p(50)<${TARGETS.apiP50Ms}`, `p(99)<${TARGETS.apiP99Ms}`],
    'http_req_failed{kind:api}': ['rate<0.01'],
  },
  setupTimeout: '2m',
};

/** Logs in once (the auth budget is tiny) and seeds one guest transfer for the public reads plus one owned transfer for the authed ones. */
export function setup() {
  const token = signupIfNeeded(required('LOAD_USER_EMAIL'), required('LOAD_USER_PASSWORD'));
  const suffix = Date.now();
  const pub = seedSmallTransfer(`k6-public-${suffix}.txt`, null);
  const owned = seedSmallTransfer(`k6-owned-${suffix}.txt`, token);
  return { token, linkId: pub.linkId, ownedId: owned.id };
}

export function publicRead(data) {
  const res = get(`/public/transfers/${data.linkId}`, null, { kind: 'api', endpoint: 'public' });
  check(res, { 'public 200': (r) => r.status === 200 });
}

export function authedRead(data) {
  const res = __ITER % 2 === 0
    ? get('/transfers', data.token, { kind: 'api', endpoint: 'list' })
    : get(`/transfers/${data.ownedId}`, data.token, { kind: 'api', endpoint: 'detail' });
  check(res, { 'authed 200': (r) => r.status === 200 });
}

export const handleSummary = summaryHandler('api-latency');
