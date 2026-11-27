// T-029 (TA-14.1 load bullet 4, TA-15): 1k-recipient email burst. k6 measures the API accept latency of the sends; the
// send -> inbox leg (p95 < 60 s) is evaluated AFTER the run with the KQL in README.md, joined on the traceparent ids in
// burst-manifest.json (k6 cannot see an inbox). Recipients must be owner-controlled mailboxes (BURST_RECIPIENT_TEMPLATE).
import { check, sleep } from 'k6';
import exec from 'k6/execution';

import { TARGETS, adminCredsOrNull, env, required, summaryHandler } from './lib/config.js';
import { adminFlag, login, seedDraftTransfer, send, traceIdFor, traceparentFor } from './lib/api.js';

const TOTAL = Number(env('TOTAL', '1000'));
const RUN_ID = env('BURST_RUN_ID', 'burst'); // make it unique per run (the manifest trace ids derive from it).
const PREP_PACE_SECONDS = Number(env('PREP_PACE_SECONDS', '1')); // spaces the draft calls of setup().

// Resolved at init so the options can be sized: sends = ceil(TOTAL / perSend). perSend comes from RECIPIENTS_PER_SEND, or
// (in setup) from the admin flag limits.free.maxEmails; at init only the env route is known, so it is required there.
const PER_SEND_ENV = env('RECIPIENTS_PER_SEND', undefined);
const SENDS = PER_SEND_ENV ? Math.ceil(TOTAL / Number(PER_SEND_ENV)) : 1;

export const options = {
  scenarios: {
    burst: { executor: 'per-vu-iterations', vus: SENDS, iterations: 1, maxDuration: '10m' },
  },
  thresholds: { 'http_req_duration{kind:send}': [`p(95)<${TARGETS.burstAcceptP95Ms}`] },
  setupTimeout: '15m',
};

const recipientFor = (template, n) => template.replace('{n}', String(n));

/** Prepares ceil(TOTAL/perSend) finalized guest drafts, each with one tiny file, so the burst itself is only the sends. */
export function setup() {
  const template = required('BURST_RECIPIENT_TEMPLATE');
  if (template.indexOf('{n}') < 0) throw new Error('BURST_RECIPIENT_TEMPLATE must contain the {n} placeholder (e.g. me+burst{n}@example.org)');
  if (!PER_SEND_ENV) {
    throw new Error('RECIPIENTS_PER_SEND is required (the plan limit limits.free.maxEmails; read it from Admin -> Flags). It sizes the VU count at init.');
  }
  const perSend = Number(PER_SEND_ENV);

  // Cross-check against the live flag when admin credentials are provided (no literal limits — golden rule 2).
  const admin = adminCredsOrNull();
  if (admin) {
    const token = login(admin.email, admin.password);
    const live = token ? adminFlag(token, 'limits.free.maxEmails') : null;
    if (live !== null && Number(live) !== perSend) {
      throw new Error(`RECIPIENTS_PER_SEND=${perSend} but limits.free.maxEmails=${live} — fix the env value`);
    }
  }

  const sends = Math.ceil(TOTAL / perSend);
  const transfers = [];
  for (let i = 0; i < sends; i++) {
    transfers.push(seedDraftTransfer(`k6-burst-${RUN_ID}-${i}.txt`));
    sleep(PREP_PACE_SECONDS);
  }
  return { template, perSend, transfers };
}

export default function (data) {
  const i = exec.vu.idInTest - 1; // 0-based send index
  const first = i * data.perSend;
  const emails = [];
  for (let n = first + 1; n <= Math.min(first + data.perSend, TOTAL); n++) emails.push(recipientFor(data.template, n));
  const res = send(data.transfers[i].id, emails, null, traceparentFor(`${RUN_ID}:${i}`), { kind: 'send' });
  check(res, { 'send 201': (r) => r.status === 201 });
}

/** Writes the manifest the post-run KQL joins on: the run id, the trace id of every send and the run window. */
export const handleSummary = summaryHandler('email-burst', (data) => {
  const perSend = Number(PER_SEND_ENV || 0);
  const sends = perSend > 0 ? Math.ceil(TOTAL / perSend) : 0;
  const durationMs = data.state && data.state.testRunDurationMs ? data.state.testRunDurationMs : null;
  const endedAt = new Date().toISOString();
  const traceIds = [];
  for (let i = 0; i < sends; i++) traceIds.push(traceIdFor(`${RUN_ID}:${i}`));
  return {
    'burst-manifest.json': JSON.stringify({ runId: RUN_ID, total: TOTAL, perSend, sends, endedAt, testRunDurationMs: durationMs, traceIds }, null, 2),
  };
});
