import { useState } from 'react'

import i18n from '../../core/i18n/init'
import { useUploadEngine, stagedHandles, type Draft } from '../../core/upload/UploadEngine'
import { getTraceParent } from '../../core/traceparent'
import { formatBytes } from '../../core/upload/formatBytes'
import { checkLimits, getPlanLimits, limitMessage } from '../../core/upload/planLimits'
import LinkScreen from '../sender/LinkScreen'
import { buildFinalizePayload, finalizeKey, type MintedFile } from './finalize'
import { useFinalize } from './finalizeState'

/**
 * Staging list (T-001-03): one row per staged file — icon, name (truncated, `title`),
 * size via `formatBytes`, remove ✕; total line "N files · X" recomputed from store
 * state on every add/remove. **Send.** is enabled only while the list is non-empty
 * and no limit violation stands (T-001-04): oversized rows are marked, a message
 * names the exact limit under the total, and Send.'s tooltip explains why it's off.
 * T-001-07 adds upload progress: an overall "Uploading… X of Y" line above the list
 * (the polite status region) plus per-row 3 px bar + percentage for rows that are
 * uploading or done, and a `--success` check on completed rows — all read live from
 * the store, no polling. T-001-08 adds row-local failure (FR-001-6): rows whose file failed
 * all per-block retries turn `--danger` — name + "Upload failed — retry" text and a Retry button
 * that re-enters the engine from the last completed block (T-001-09). Send. now posts every staged file to
 * /api/v1/transfers/draft in row order and hands the minted draft to the engine's start().
 */

 // T-001-09 (Send wiring): the API is cross-origin at a fixed local port — no proxy, no env vars yet (ADR row).
 const DRAFT_ENDPOINT = 'http://localhost:8080/api/v1/transfers/draft'
 // T-001-10 (TA-4.2#2): finalize — server-side blob copy staging→transfers + mint linkId; {draftId} filled per send().
 const FINALIZE_ENDPOINT = 'http://localhost:8080/api/v1/transfers/draft/{draftId}/finalize'
 const AZURITE_ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
 const AZURITE_ACCOUNT_NAME = 'devstoreaccount1'

 /**
  * T-001-10 (TA-4.2#2): finalize response wire shape — mirrors Program.cs L229 exactly (id in 'n'-form, status "Draft").
  * linkId + id are what LinkScreen needs to render the public URL and activate endpoint 3 on send.
  */
 type FinalizeResult = {
   id: string
   linkId: string
   status: string
   expiresAt: string
   totalBytes: number
   files: Array<{ id: string; name: string }>
 }

 /**
  * Re-signs each minted uploadUrl for a plain-HTTP browser leg. Azurite (and the C# integration fixture) rejects a PUT on a
  * spr=https SAS unless the protocol line is re-signed as "https,http" — same string-to-sign rules as BlobSasMinter, only the
  * protocol row changes. Minted values are otherwise untouched.
  */
 const reSignForHttp = async (draft: Draft): Promise<Draft> => {
   const key = await crypto.subtle.importKey(
     'raw',
     Uint8Array.from(atob(AZURITE_ACCOUNT_KEY_B64), (c) => c.charCodeAt(0)),
     { name: 'HMAC', hash: 'SHA-256' },
     true,
     ['sign'],
   )
  const files = await Promise.all(
    draft.files.map(async (f) => ({ ...f, uploadUrl: await reSignUploadUrl(f.uploadUrl, key) })),
  )
   return { ...draft, files }
 }

 const toBase64 = (buf: ArrayBuffer) => btoa(String.fromCharCode(...new Uint8Array(buf)))

 const reSignUploadUrl = async (uploadUrl: string, key: CryptoKey): Promise<string> => {
   const u = new URL(uploadUrl)
   const st = u.searchParams.get('st') ?? ''
   const se = u.searchParams.get('se') ?? ''
   // The minted path already starts with /devstoreaccount1 — the canonical name is "/blob/" + account + that (C# fixture, DraftEndpointTests).
   const lines = [
     'cwr', st, se, '/blob/' + AZURITE_ACCOUNT_NAME + u.pathname, '', '', 'https,http',
     '2024', 'b', '', '', '', '', '', '', '',
   ]
   const sig = toBase64(
     await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(lines.join('\n'))),
   )
   return `${u.origin}/${AZURITE_ACCOUNT_NAME}${u.pathname}?sv=2024&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=cwr&sig=${encodeURIComponent(sig)}`
 }

export function StagingList() {
  const files = useUploadEngine((s) => s.files)
  const overall = useUploadEngine((s) => s.overall)
  const removeFile = useUploadEngine((s) => s.remove)
  const retryFile = useUploadEngine((s) => s.retry)
  const [sending, setSending] = useState(false) // double-click guard — start() itself serializes files (TA-8.3).
  // T-001-10 handoff: shared store so App can hide the DropZone once LinkScreen is showing (see finalizeState.ts).
  const finalized = useFinalize((s) => s.finalized)
  const setFinalized = useFinalize((s) => s.setFinalized)
  const finalizeError = useFinalize((s) => s.finalizeError)
  const setFinalizeError = useFinalize((s) => s.setFinalizeError)
  const clearFinalize = useFinalize((s) => s.clear)

  // "Send again" (LinkScreen sent-phase): reset finalize + staged files so staging reappears for the next transfer.
  const handleSendAgain = (): void => {
    clearFinalize()
    useUploadEngine.getState().reset() // clears staged files + FR-001-6 records so staging reappears.
  }

  // Pure arithmetic on staged sizes; re-read every render (TA-8.3).
  const limits = getPlanLimits()
  const check = checkLimits(files, limits)
  const message = limitMessage(files, check, limits)
  // T-001-09 (Send wiring): post the whole staged selection in row order — start() mints uploadUrls by position.
  // T-001-10: capture each file's minted fileId as it uploads so finalize can build its payload; after all files are
  // done, POST /finalize with an Idempotency-Key (EC-002-1) and swap the staging list for LinkScreen.
  const send = async () => {
    if (sending || finalized !== null) return
    setSending(true)
    setFinalizeError(false)
    try {
      const rows = files.map((f) => ({
        name: f.name,
        sizeBytes: f.size,
        contentType: stagedHandles.get(f.id)?.type || null,
      }))
      const res = await fetch(DRAFT_ENDPOINT, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
        body: JSON.stringify({ files: rows }),
      })
      if (!res.ok) return // 422 Problem+JSON (TRANSFER_SIZE_EXCEEDED): the limit message already explains it; keep minimal.
      const draft = (await res.json()) as Draft
      // Minted fileIds by staged-row position — Program.cs L213 Guid.Parse's each one against this list.
      const minted: MintedFile[] = files.map((f, i) => ({
        stagedId: f.id,
        name: f.name,
        sizeBytes: f.size,
        fileId: draft.files[i]?.fileId ?? '',
      }))
      await useUploadEngine.getState().start(await reSignForHttp(draft))
      // start() never rejects — per-file failures surface as 'failed' status. Finalize only when every file is done;
      // a failed row means the user retries it (T-001-08) before finalize, so don't mint a partial transfer.
      const allDone = files.length > 0 && useUploadEngine.getState().files.every((f) => f.status === 'done')
      if (!allDone) return
      const finRes = await fetch(FINALIZE_ENDPOINT.replace('{draftId}', draft.draftId), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': finalizeKey(draft.draftId), traceparent: getTraceParent() },
        body: JSON.stringify(buildFinalizePayload(minted)),
      })
      if (!finRes.ok) { setFinalizeError(true); return } // inline error below — the blobs are staged; retry re-finalizes (EC-002-1).
      const result = (await finRes.json()) as FinalizeResult
      setFinalized(result)
    } finally {
      setSending(false)
    }
  }

  if (finalized !== null) {
    // T-001-10 handoff: the hero card — full public URL + Copy + recipients/sender fields; endpoint 3 activates on send.
    // "Send again" clears finalize + staged files → App re-renders the drop zone for the next transfer (same as ReSendScreen's Upload path).
    return <LinkScreen linkId={finalized.linkId} transferId={finalized.id} onSendAgain={() => handleSendAgain()} />
  }

  if (finalizeError) {
    // EC-002-1: finalize failed — blobs are staged, so retrying re-finalizes with the same key; no half-filled form.
    return (
      <section className="sl" aria-label={i18n.t('landing.staging.finalizeFailed')}>
        <p role="alert" className="sl-limit-msg">{i18n.t('landing.staging.finalizeFailedMsg')}</p>
        <div className="sl-footer">
          <button type="button" className="btn-primary sl-send" onClick={() => void send()}>{i18n.t('landing.staging.tryAgain')}</button>
        </div>
      </section>
    )
  }

  return (
    <section className="sl" aria-label={i18n.t('landing.staging.sectionLabel')}>
      {/* Overall line (FR-001-5): combined bytes uploaded / total, recomputed on every store transition. */}
      {/* role=status (polite): the text re-renders only on store transitions — block completions / status changes — never per byte (F-TRF-016-3). */}
      {files.length > 0 && (
        <p className="sl-overall" role="status">{i18n.t('landing.staging.uploading', { done: formatBytes(overall.sentBytes), total: formatBytes(overall.totalBytes) })}</p>
      )}
      <ul className="sl-list">
        {files.map((f) => (
          <li key={f.id} className={`sl-row${check.overFileIds.has(f.id) ? ' sl-row-over' : ''}${f.status === 'failed' ? ' sl-row-failed' : ''}${f.status === 'done' ? ' sl-done' : ''}`}>
            <span className="sl-icon" aria-hidden="true">
              {/* generic file icon */}
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9Z" />
                <path d="M14 3v6h5" />
              </svg>
            </span>
            <span className="sl-name" title={f.name}>{f.name}</span>
            <span className="sl-size">{formatBytes(f.size)}</span>
            {(f.status === 'uploading' || f.status === 'done') && (
              <>
                <span className="sl-percent">{Math.round(f.progress * 100)}%</span>
                {/* 3 px bar pinned to the row's bottom edge; aria-hidden — the percent span carries the value. */}
                <div className="sl-progress-track" aria-hidden="true">
                  <div className="sl-bar" style={{ width: `${f.progress * 100}%` }} />
                </div>
              </>
            )}
            {f.status === 'done' && <span className="sl-done" aria-hidden="true">✓</span>}
            {/* FR-001-6 (T-001-08): row-local failure — danger text + Retry; the engine resumes from completed blocks. */}
            {/* T-013-04 (FR-013-3): a blob-503 failure after the retry ladder renders the degraded copy instead. */}
            {f.status === 'failed' && (
              <>
                <span className="sl-failed-msg" aria-live="polite">{f.isBlobUnavailable ? i18n.t('landing.dropzone.uploadDegraded') : i18n.t('landing.staging.failedMsg')}</span>
                <button type="button" className="sl-retry" aria-label={i18n.t('landing.staging.retryAria', { name: f.name })} onClick={() => void retryFile(f.id)}>{i18n.t('landing.staging.retryButton')}</button>
              </>
            )}
            <button
              type="button"
              className="sl-remove"
              aria-label={i18n.t('landing.staging.removeFile', { name: f.name })}
              onClick={() => removeFile(f.id)}
            >✕</button>
          </li>
        ))}
      </ul>
      <p className="sl-total">{files.length === 1 ? i18n.t('landing.staging.totalSingle', { total: formatBytes(overall.totalBytes) }) : i18n.t('landing.staging.totalMany', { count: files.length, total: formatBytes(overall.totalBytes) })}</p>
      {message && (
        <p className="sl-limit-msg" role="alert">
          {message}
        </p>
      )}
      <div className="sl-footer">
        {/* Disabled buttons don't fire their own mouse events — the span wrapper carries title. */}
        <span className="sl-send-wrap" title={message ?? undefined}>
          <button type="button" className="btn-primary sl-send" onClick={() => void send()} disabled={files.length === 0 || message !== null}>{i18n.t('landing.staging.send')}</button>
        </span>
      </div>
    </section>
  )
}

export default StagingList
