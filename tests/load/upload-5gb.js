// T-029 (TA-14.1 load bullet 3, TA-15): 5 GB upload in < 10 minutes. One VU, one iteration: draft as the load user -> 8 MiB blocks (4 in
// flight) straight to Blob -> Put Block List -> finalize. The size is the plan's maxTransferSize (read from the API, no literal).
import { Trend } from 'k6/metrics';
import { check } from 'k6';

import { TARGETS, required, summaryHandler } from './lib/config.js';
import { BLOCK_SIZE, commitBlockList, createDraft, finalize, planUsage, putBlocks, signupIfNeeded } from './lib/api.js';

const uploadDuration = new Trend('upload_duration_ms', true);
const uploadMbps = new Trend('upload_mbps'); // reported, not gated: a runner's uplink is not a 100 Mbps line (README).

export const options = {
  scenarios: { upload: { executor: 'per-vu-iterations', vus: 1, iterations: 1, maxDuration: '40m' } },
  thresholds: { upload_duration_ms: [`max<${TARGETS.uploadMaxMs}`], 'http_req_failed{kind:blob}': ['rate<0.01'], checks: ['rate>0.99'] },
  setupTimeout: '2m',
};

/**
 * The upload is a GUEST draft + finalize (no token): an owned finalize would check active bytes + 5 GiB against the storage quota.
 * Guest drafts always use the FREE plan's limits (CreateDraftHandler), and billing/plan needs a session, so the sizes are read from
 * the load user's plan after asserting it IS the free plan (one login).
 */
export function setup() {
  const token = signupIfNeeded(required('LOAD_USER_EMAIL'), required('LOAD_USER_PASSWORD'));
  const plan = planUsage(token);
  if (plan.planCode !== 'free') throw new Error(`load user is on plan "${plan.planCode}", not the guest default (free) — use a free-plan account`);
  const limits = plan.limits;
  return { totalBytes: limits.maxTransferSize, maxSingleFile: limits.maxSingleFile };
}

export default function (data) {
  // Split into files no larger than the single-file limit; the transfer total is the plan's maxTransferSize.
  const sizes = [];
  for (let left = data.totalBytes; left > 0; left -= data.maxSingleFile) sizes.push(Math.min(left, data.maxSingleFile));
  const files = sizes.map((sizeBytes, i) => ({ name: `k6-upload-${Date.now()}-${i}.bin`, sizeBytes, contentType: 'application/octet-stream' }));

  const block = new ArrayBuffer(BLOCK_SIZE); // allocated ONCE, reused for every block of every file.
  const started = Date.now();

  const draft = createDraft(files, null);
  draft.files.forEach((f, i) => {
    const ids = putBlocks(f.uploadUrl, files[i].sizeBytes, block);
    commitBlockList(f.uploadUrl, ids, files[i].contentType);
  });
  const fin = finalize(draft.draftId, draft.files.map((f, i) => Object.assign({ fileId: f.fileId }, files[i])), null); // 600 s: server-side copy.

  const elapsed = Date.now() - started;
  uploadDuration.add(elapsed);
  uploadMbps.add((data.totalBytes * 8) / 1e6 / (elapsed / 1000));
  check(fin, { 'finalized': (f) => typeof f.linkId === 'string' });
}

export const handleSummary = summaryHandler('upload-5gb');
