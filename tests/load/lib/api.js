// T-029: k6 helpers for the ProtoDrop public API and Azure Blob — the one seam every load script goes through.
// Built-ins only: k6/http, k6/encoding, k6/crypto.
import http from 'k6/http';
import { fail } from 'k6';
import encoding from 'k6/encoding';
import crypto from 'k6/crypto';

import { API_BASE_URL } from './config.js';

/** Block size and parallelism match the SPA (e2e/helpers.ts BLOCK_SIZE, UploadEngine MAX_PARALLEL_BLOCKS). */
export const BLOCK_SIZE = 8 * 1024 * 1024;
export const MAX_PARALLEL_BLOCKS = 4;

const url = (path) => `${API_BASE_URL()}/api/v1${path}`;

/** Explicit session cookie header (the cookie is Secure; an explicit header works on any scheme). */
export function sessionHeaders(token) {
  return token ? { Cookie: `wa.session=${token}` } : {};
}

function jsonParams(token, extraHeaders, tags, timeout) {
  return {
    headers: Object.assign({ 'Content-Type': 'application/json' }, sessionHeaders(token), extraHeaders || {}),
    tags: tags || {},
    timeout: timeout || '60s',
  };
}

function mustOk(res, expected, what) {
  const ok = Array.isArray(expected) ? expected.includes(res.status) : res.status === expected;
  if (!ok) fail(`${what} failed: ${res.status} ${String(res.body).slice(0, 300)}`);
  return res;
}

/** Login; returns the session token or null (caller decides). */
export function login(email, password) {
  const res = http.post(url('/auth/login'), JSON.stringify({ email, password }), jsonParams());
  return res.status === 200 ? res.json('sessionToken') : null;
}

/** Login, creating the account on the first run (a wrong password on an existing account fails loudly). Spends <= 3 auth calls. */
export function signupIfNeeded(email, password) {
  let token = login(email, password);
  if (token) return token;
  const res = http.post(url('/auth/signup'), JSON.stringify({ email, password }), jsonParams());
  mustOk(res, 200, 'signup');
  token = res.json('sessionToken');
  return token;
}

/** The caller's plan limits (billing/plan) — sizes and counts come from the API, never from literals here. */
export function planLimits(token) {
  const res = mustOk(http.get(url('/billing/plan'), jsonParams(token)), 200, 'billing/plan');
  return res.json('limits');
}

/** Reads one admin flag value (needs an admin session); returns a string or null when not readable. */
export function adminFlag(adminToken, key) {
  const res = http.get(url('/admin/flags'), jsonParams(adminToken));
  if (res.status !== 200) return null;
  const row = res.json().find((r) => r.key === key);
  return row ? String(row.value) : null;
}

/** POST /transfers/draft -> { draftId, files:[{fileId, uploadUrl}] }. */
export function createDraft(files, token) {
  const res = mustOk(http.post(url('/transfers/draft'), JSON.stringify({ files }), jsonParams(token)), 201, 'draft');
  return res.json();
}

/** Single-shot Put Blob (small files). */
export function putBlob(uploadUrl, body, contentType) {
  const res = http.put(uploadUrl, body, {
    headers: { 'x-ms-blob-type': 'BlockBlob', 'x-ms-version': '2024-08-04', 'Content-Type': contentType || 'application/octet-stream' },
    timeout: '120s',
  });
  return mustOk(res, 201, 'put blob');
}

const blockId = (i) => encoding.b64encode(String(i).padStart(6, '0')); // equal-length ids, as Azure requires.

/**
 * Streams `totalBytes` to a staging blob as 8 MiB blocks, 4 in flight per round (http.batch). ONE zero-filled ArrayBuffer is
 * allocated once and reused for every block, so a 5 GB upload never sits in memory. Returns the committed block ids.
 */
export function putBlocks(uploadUrl, totalBytes, blockBuf) {
  const count = Math.ceil(totalBytes / BLOCK_SIZE);
  const ids = [];
  for (let first = 0; first < count; first += MAX_PARALLEL_BLOCKS) {
    const batch = [];
    for (let i = first; i < Math.min(first + MAX_PARALLEL_BLOCKS, count); i++) {
      const id = blockId(i);
      ids.push(id);
      const size = i === count - 1 ? totalBytes - i * BLOCK_SIZE : BLOCK_SIZE;
      batch.push([
        'PUT',
        `${uploadUrl}&comp=block&blockid=${encodeURIComponent(id)}`,
        size === BLOCK_SIZE ? blockBuf : blockBuf.slice(0, size),
        { headers: { 'x-ms-version': '2024-08-04' }, timeout: '120s', tags: { kind: 'blob' } },
      ]);
    }
    for (const res of http.batch(batch)) mustOk(res, 201, 'put block');
  }
  return ids;
}

/** Put Block List — commits the uploaded blocks as the blob. */
export function commitBlockList(uploadUrl, ids, contentType) {
  const body = '<?xml version="1.0" encoding="utf-8"?><BlockList>' + ids.map((id) => `<Latest>${id}</Latest>`).join('') + '</BlockList>';
  const res = http.put(`${uploadUrl}&comp=blocklist`, body, {
    headers: { 'x-ms-version': '2024-08-04', 'Content-Type': 'application/xml', 'x-ms-blob-content-type': contentType || 'application/octet-stream' },
    timeout: '120s',
    tags: { kind: 'blob' },
  });
  return mustOk(res, 201, 'put block list');
}

/** POST finalize (server-side copy staging -> transfers; slow for 5 GB, hence the long timeout). Returns the transfer. */
export function finalize(draftId, files, token, timeout) {
  const body = JSON.stringify({ files });
  const params = jsonParams(token, { 'Idempotency-Key': `k6-${draftId}` }, { kind: 'finalize' }, timeout || '600s');
  return mustOk(http.post(url(`/transfers/draft/${draftId}/finalize`), body, params), 201, 'finalize').json();
}

/** POST send. `emails` may be empty (link-only). `traceparent` correlates the send with server telemetry (TA-10). */
export function send(transferId, emails, token, traceparent, tags) {
  const headers = { 'Idempotency-Key': `k6-send-${transferId}` };
  if (traceparent) headers.traceparent = traceparent;
  const body = JSON.stringify({ emails, senderName: 'k6 load' });
  return http.post(url(`/transfers/${transferId}/send`), body, jsonParams(token, headers, tags));
}

/** GET with the kind tag the thresholds select on. */
export function get(path, token, tags) {
  return http.get(url(path), jsonParams(token, undefined, tags));
}

/** Guest seed of a tiny single-file transfer -> { id, linkId, fileId }. Active (sent link-only). */
export function seedSmallTransfer(name, token) {
  const content = `k6 load seed ${name}\n`;
  const file = { name, sizeBytes: content.length, contentType: 'text/plain' };
  const draft = createDraft([file], token);
  putBlob(draft.files[0].uploadUrl, content, 'text/plain');
  const fin = finalize(draft.draftId, [Object.assign({ fileId: draft.files[0].fileId }, file)], token, '120s');
  mustOk(send(fin.id, [], token, null, {}), 201, 'send');
  return { id: fin.id, linkId: fin.linkId, fileId: draft.files[0].fileId };
}

/** Same as seedSmallTransfer but leaves the transfer a Draft (the email burst sends it later). */
export function seedDraftTransfer(name) {
  const content = `k6 burst seed ${name}\n`;
  const file = { name, sizeBytes: content.length, contentType: 'text/plain' };
  const draft = createDraft([file], null);
  putBlob(draft.files[0].uploadUrl, content, 'text/plain');
  const fin = finalize(draft.draftId, [Object.assign({ fileId: draft.files[0].fileId }, file)], null, '120s');
  return { id: fin.id, linkId: fin.linkId };
}

const hex = (s) => crypto.sha1(s, 'hex');

/** W3C traceparent from a deterministic seed: 32-hex trace id, 16-hex span id (sampled). */
export function traceparentFor(seed) {
  return `00-${crypto.md5(seed, 'hex')}-${hex('span:' + seed).slice(0, 16)}-01`;
}

/** The trace id of {@link traceparentFor}`(seed)` — what the README KQL joins on. */
export function traceIdFor(seed) {
  return crypto.md5(seed, 'hex');
}

/** Random but valid traceparent (for scripts that do not need to reproduce the id later). */
export function newTraceparent() {
  const hexOf = (n) => Array.from(new Uint8Array(crypto.randomBytes(n)), (b) => b.toString(16).padStart(2, '0')).join('');
  return `00-${hexOf(16)}-${hexOf(8)}-01`;
}
