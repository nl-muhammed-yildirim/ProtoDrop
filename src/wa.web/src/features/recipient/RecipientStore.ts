import { API_BASE } from '../../core/config/apiBase'
import { create } from 'zustand'
import { getTraceParent } from '../../core/traceparent'
import { trackClientEvent } from '../../core/telemetry/clientTelemetry'

/**
 * T-003-02 (TA-4.2#4): Zustand store for the recipient page at /t/{linkId} — holds endpoint 4's payload
 * (from, note, files, hasDownloadAll, downloadsLeft, passwordRequired) + status (loading/active/error).
 * load(linkId) fetches GET /api/v1/public/transfers/{linkId} with an AbortController + 30 s timeout; errors land in
 * state and never reject (UploadEngine convention), so the page view surfaces no unhandled rejection.
 * T-003-03 (FR-003-5/8/9, TA-4.1.3): terminal states — !res.ok is classified by the closed Problem+JSON code list:
 * TRANSFER_EXPIRED → 'expired' (covers both 410 expired and 404 unknown — FR-003-9 keeps those screens identical),
 * MAX_DOWNLOADS_REACHED → 'downloadLimit'; any other body (5xx, network) falls through to the generic error state.
 * terminalMessage holds the server's Problem+JSON details verbatim — TA-4.1.3's only user-facing string slot;
 * client copy exists only as a fallback when details is missing. downloadsLeft renders at T-003-03.
 * T-003-05 (FR-003-6, AC-003-2 client half): unlock(linkId, password) POSTs endpoint 5 ({ password } body);
 * correct → the 7-day JWT lands in sessionStorage under pd.unlock.{linkId} (tab-isolated by design — a second
 * tab or a cleared store re-prompts, EC-003-2) and load() re-queries endpoint 4 with ?t= so a refresh in the same
 * tab never re-prompts (TA-4.2#5). Terminal codes win over the gate (T-003-03 precedence — same classifyProblem).
 * T-004-03 (FR-004-6, EC-004-4): downloadAll(linkId) drives endpoint 7 — 200 {url} navigates like a per-file download,
 * 202 {pollAfterSec} moves to 'preparing' and re-polls at the server's interval until 200 or the 3-minute ceiling;
 * terminal Problem codes win (same screens as per-file), any other failure lands downloadAllFailed (toast, per-file rows stay usable).
 */

export interface RecipientFile { fileId: string; name: string; sizeBytes: number }

export type RecipientStatus = 'loading' | 'active' | 'error' | 'expired' | 'downloadLimit'

// T-003-02 (TA-4.2#4): endpoint 4 — fixed-port style like LinkScreen/StagingList; page views mint no SAS, no auth, no cookie.
const PUBLIC_TRANSFER_ENDPOINT = `${API_BASE}/public/transfers/{linkId}`
// T-003-05 (TA-4.2#5): endpoint 5 — the only client-side consumer of the unlock token; no auth, no cookie, no SAS.
const UNLOCK_ENDPOINT = `${API_BASE}/public/transfers/{linkId}/unlock`
// T-003-06 (TA-4.2#6): endpoint 6 — mint a 30-minute read SAS; the browser streams the blob directly against storage.
const DOWNLOAD_URL_ENDPOINT = `${API_BASE}/public/transfers/{linkId}/files/{fileId}/download-url`
// T-004-03 (TA-4.2#7): endpoint 7 — zip download URL; 200 {url} | 202 {pollAfterSec}.
const DOWNLOAD_ALL_URL_ENDPOINT = `${API_BASE}/public/transfers/{linkId}/download-all-url`
const LOAD_TIMEOUT_MS = 30_000
// T-004-03 (FR-004-6): fallback poll interval when a 202 omits pollAfterSec (the server sends the same 2 s) - UX timing, not a plan limit.
const DEFAULT_POLL_AFTER_SEC = 2
// T-004-03 (US-004-01 alt flow): give up after 3 minutes of 202s, measured from the first 202 - UX timing, not a plan limit.
const ZIP_PREPARE_TIMEOUT_MS = 180_000
const ERROR_MESSAGE = 'Something went wrong'

// T-003-08-fix (T-055-05/TA-14.3, StagingList precedent): the local Azurite account is IP-hosted — the minted URL's
// authority drops the /devstoreaccount1 path segment (raw GET 404s), and its spr=https signature rejects plain HTTP
// (AuthorizationProtocolMismatch). Recipient downloads re-address + protocol-re-sign in product code, exactly like
// StagingList's uploads and the e2e seed helper. TA-8.2: features are self-contained — same constants live there too.
const AZURITE_BLOB_ORIGIN = 'http://127.0.0.1:10000'
const AZURITE_ACCOUNT_NAME = 'devstoreaccount1'
const AZURITE_ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='

const toBase64 = (buf: ArrayBuffer) => btoa(String.fromCharCode(...new Uint8Array(buf)))

/**
 * T-003-08-fix (T-055-05/TA-14.3 convention, StagingList precedent for uploads): re-addresses a minted recipient read
 * SAS to the canonical /blob/devstoreaccount1{path} name and re-signs over the blob-SAS string-to-sign with only the
 * protocol line changed to "https,http" — Azurite reads the first path segment as the account and rejects plain-HTTP
 * GETs against a spr=https signature. The minted value is otherwise untouched (same sv/st/se/sp/sr/rscd), including the
 * RFC 5987 rscd override which IS signed into the string-to-sign (T-003-06, element 13 of the 16-line form).
 */
const browserDownloadUrl = async (mintedUrl: string): Promise<string> => {
  const minted = new URL(mintedUrl)
  const q = minted.searchParams
  // The minted query carries these (TA-3.6 matrix) — the fallbacks are the minter's literal defaults.
  const sv = q.get('sv') ?? '2024'
  const st = q.get('st') ?? ''
  const se = q.get('se') ?? ''
  const sp = q.get('sp') ?? 'r'
  const sr = q.get('sr') ?? 'b'
  const rscd = q.get('rscd') ?? ''

  const key = await crypto.subtle.importKey(
    'raw',
    Uint8Array.from(atob(AZURITE_ACCOUNT_KEY_B64), (c) => c.charCodeAt(0)),
    { name: 'HMAC', hash: 'SHA-256' },
    true,
    ['sign'],
  )

  // Blob-SAS canonical string-to-sign (16 lines): sp, st, se, canonical resource, identifier, iprange, protocol,
  // sv, sr, snapshot, encryption scope, cache-control, content-disposition (the signed rscd override), encoding,
  // language, content-type — same layout as BlobSasMinter/DownloadUrlEndpointTests.StreamUrl.
  const lines = [
    sp, st, se, `/blob/${AZURITE_ACCOUNT_NAME}${minted.pathname}`, '', '', 'https,http', sv, sr, '', '', '', rscd, '', '', '',
  ]
  const sig = toBase64(
    await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(lines.join('\n'))),
  )

  return `${AZURITE_BLOB_ORIGIN}/${AZURITE_ACCOUNT_NAME}${minted.pathname}?sv=${sv}&spr=https%2Chttp&st=${encodeURIComponent(st)}&se=${encodeURIComponent(se)}&sr=b&sp=${sp}&rscd=${encodeURIComponent(rscd)}&sig=${encodeURIComponent(sig)}`
}

// T-003-05 (AC-003-2): the unlock token is tab-isolated in sessionStorage by design (not a cookie — a second tab,
// or a cleared store, re-prompts: EC-003-2 documented trade-off). Empty string is treated as absent so a stray
// write never counts as a valid token.
function tokenKey(linkId: string): string {
  return `pd.unlock.${linkId}`
}
function readUnlockToken(linkId: string): string | null {
  const stored = window.sessionStorage.getItem(tokenKey(linkId))
  return stored !== null && stored.length > 0 ? stored : null
}

// T-003-03 (TA-4.1.3): endpoint 4's terminal Problem+JSON codes — the closed list pinned at wa.domain/Errors/ProblemCodes.cs.
// Literal client strings by design: the store never imports server constants; RecipientPage.test.tsx pins both, so a
// server rename surfaces in CI as a test failure instead of a silent generic-error fallback.
const TERMINAL_CODE_EXPIRED = 'TRANSFER_EXPIRED'
const TERMINAL_CODE_DOWNLOAD_LIMIT = 'MAX_DOWNLOADS_REACHED'

// T-003-05 (TA-4.1.3): endpoint 5's wrong-password Problem+JSON code — same closed list, pinned by the tests below.
const WRONG_PASSWORD_CODE = 'WRONG_PASSWORD'
// Client-only fallback copy when a 401 omits details — the server title is "Wrong password" (ProblemCodes.cs).
const WRONG_PASSWORD_MESSAGE = 'Wrong password'

// Client-only fallback copy when the Problem+JSON omits details — server copy is authoritative (TA-4.1.3's string slot).
const TERMINAL_MESSAGES: Record<'expired' | 'downloadLimit', string> = {
  expired: 'This transfer has expired.',
  downloadLimit: 'All downloads have been used.',
}

// T-003-05 (T-003-03 precedence): shared terminal classification for endpoint 4's and endpoint 5's Problem+JSON —
// the same closed codes map to the same screens, so a terminal status always wins over the password gate.
function classifyProblem(
  problem: { code?: string; details?: string } | null,
): { status: 'expired' | 'downloadLimit'; message: string } | null {
  const status = problem?.code === TERMINAL_CODE_EXPIRED ? 'expired'
    : problem?.code === TERMINAL_CODE_DOWNLOAD_LIMIT ? 'downloadLimit'
    : null
  if (status === null) return null
  const details = typeof problem?.details === 'string' && problem.details.length > 0
    ? problem.details
    : TERMINAL_MESSAGES[status]
  return { status, message: details }
}

interface RecipientState {
  status: RecipientStatus
  from: string
  note: string | null
  files: RecipientFile[]
  hasDownloadAll: boolean
  /** null = unlimited — the wire shape is held here (TA-4.2#4); rendered as "Downloads left: N" (T-003-03). */
  downloadsLeft: number | null
  /** Gate UI is T-003-05 — the store's unlock() flips it via endpoint 4's ?t= token. */
  passwordRequired: boolean
  /** F-PRF-001: sender's organization (null when absent or the sender's plan lacks Branding). */
  orgName: string | null
  orgLogoUrl: string | null
  /** F-PRF-002 (FR-022-6): UTC ISO due time of a Scheduled transfer; null otherwise. */
  scheduledSendAt: string | null
  /** F-PRF-004 (FR-024-3): server-resolved; false on every non-active shape. */
  adsEnabled: boolean
  /** T-003-03: Problem+JSON details verbatim — the screen's single line on terminal screens (TA-4.1.3). */
  terminalMessage: string | null
  /** T-003-05 (AC-003-2): true while endpoint 5 is in flight — the gate keeps rendering, submit disabled. */
  unlocking: boolean
  /** T-003-05 (AC-003-2): wrong-password alert text (server details or fallback copy); null = no error. */
  unlockError: string | null
  /** T-003-06: the fileId currently downloading — its row renders the in-flight indicator (UI notes). */
  downloadingFileId: string | null
  /** T-003-06 (US-003-03 alt flow): the fileId whose mint failed — its row shows "File temporarily unavailable." */
  downloadErrorFileId: string | null
  /** T-003-07 (FR-003-7): fileIds completed via endpoint 6's success path — monotonic within a page instance. */
  downloadedFileIds: string[]
  /** T-003-07 (FR-003-7): edge-triggered when a full download completes on an active transfer — the panel then stays visible. */
  growthPanelVisible: boolean
  /** T-004-03: 'requesting' = first endpoint-7 call in flight (label unchanged - cached zip never shows preparing); 'preparing' after a 202. */
  downloadAllPhase: 'idle' | 'requesting' | 'preparing'
  /** T-004-03 (US-004-01 alt flow): drives the "Download preparation failed" toast; cleared when a new downloadAll starts. */
  downloadAllFailed: boolean
  error: string | null
  /**
   * T-013-02 (AC-013-1 surface): the captured Problem+JSON from load()'s !res.ok path, when the body carried a
   * closed-list code. A 500 / INTERNAL is NOT terminal — it still lands in the generic 'error' status, but this
   * field lets RecipientPage render ErrorScreen(500) with the Ref line instead of the plain .rp-error markup.
   */
  capturedProblem: { code: string; correlationId?: string } | null
}

interface RecipientActions {
  load(linkId: string): Promise<void>
  /** T-003-05 (AC-003-2): POST endpoint 5 with the password; success stores the token and re-queries endpoint 4. */
  unlock(linkId: string, password: string): Promise<void>
  /** T-003-06: GET endpoint 6 with the stored ?t= token → browser navigates to the minted SAS (streams directly from storage). */
  download(linkId: string, fileId: string): void
  /** T-004-03 (FR-004-6): GET endpoint 7, polling at the server's pollAfterSec until the zip URL is ready, then navigate. */
  downloadAll(linkId: string): void
  /** T-004-03: clears the failure toast flag. */
  dismissDownloadAllError(): void
  /** Aborts the in-flight load and restores the loading shape — keeps renders hermetic (UploadEngine reset style). */
  reset(): void
}

// One transfer page at a time; a new load (or reset) aborts the previous request.
let activeController: AbortController | null = null
// T-004-03: the in-flight endpoint-7 request and the pending poll timer - reset()/a new downloadAll cancel both.
let downloadAllController: AbortController | null = null
let downloadAllTimer: ReturnType<typeof setTimeout> | null = null
function cancelDownloadAll(): void {
  downloadAllController?.abort()
  downloadAllController = null
  if (downloadAllTimer !== null) clearTimeout(downloadAllTimer)
  downloadAllTimer = null
}

const LOADING_SHAPE = {
  status: 'loading' as RecipientStatus,
  from: '',
  note: null as string | null,
  files: [] as RecipientFile[],
  hasDownloadAll: false,
  downloadsLeft: null as number | null,
  passwordRequired: false,
  orgName: null,
  orgLogoUrl: null,
  scheduledSendAt: null as string | null,
  adsEnabled: false,
  terminalMessage: null as string | null,
  unlocking: false,
  unlockError: null as string | null,
  downloadingFileId: null as string | null,
  downloadErrorFileId: null as string | null,
  downloadedFileIds: [] as string[],
  growthPanelVisible: false,
  downloadAllPhase: 'idle' as 'idle' | 'requesting' | 'preparing',
  downloadAllFailed: false,
  error: null as string | null,
  capturedProblem: null as { code: string; correlationId?: string } | null,
}

export const useRecipientStore = create<RecipientState & RecipientActions>((set, get) => {
  // T-003-07 (FR-003-7): "full download" = every requested file completed - client-side state only, no round-trip.
  // Shared by per-file and zip completion (T-004-03: zip passes every fileId). Re-downloads don't re-append; partials never flip it.
  const markCompleted = (fileIds: string[]): void => {
    const { status, files, downloadedFileIds, growthPanelVisible } = get()
    const nextDownloadedFileIds = [...downloadedFileIds, ...fileIds.filter((id) => !downloadedFileIds.includes(id))]
    const fullyDownloaded = files.length > 0 && files.every((f) => nextDownloadedFileIds.includes(f.fileId))
    set({
      downloadedFileIds: nextDownloadedFileIds,
      // Edge-triggered once when the predicate flips false->true; a reload clears both fields (fresh page view is a
      // fresh potential announcement), so re-downloads after the flip never hide or re-announce the panel.
      growthPanelVisible: status === 'active' && (growthPanelVisible || fullyDownloaded),
    })
  }
  return {
  ...LOADING_SHAPE,
  load: (linkId) =>
    (async () => {
      activeController?.abort() // a re-mount or route change supersedes the previous load.
      const controller = new AbortController()
      activeController = controller
      let timedOut = false // an abort from the timeout is a real error; an abort from reset() is not.
      const timer = setTimeout(() => { timedOut = true; controller.abort() }, LOAD_TIMEOUT_MS)
      set({ ...LOADING_SHAPE })
      try {
        // T-003-05 (TA-4.2#5): a same-tab stored unlock token re-queries ungated — a refresh never re-prompts.
        const token = readUnlockToken(linkId)
        const url = PUBLIC_TRANSFER_ENDPOINT.replace('{linkId}', linkId)
          + (token !== null ? `?t=${encodeURIComponent(token)}` : '')
        const res = await fetch(url, { signal: controller.signal, headers: { traceparent: getTraceParent() } })
        if (!res.ok) {
          // T-003-03 (FR-003-8/9): classify by the Problem+JSON code, not the HTTP status — 410 and 404 both map to
          // 'expired' when the code is TRANSFER_EXPIRED; a non-Problem body (or parse failure) keeps the generic error.
          const problem = (await res.json()) as { code?: string; details?: string; correlationId?: string } | null
          const terminal = classifyProblem(problem)
          if (terminal !== null) {
            // Terminal states carry no payload — LOADING_SHAPE already reset every active field, so nothing leaks.
            set({ ...LOADING_SHAPE, status: terminal.status, terminalMessage: terminal.message })
            return
          }
          // T-013-02 (AC-013-1 surface): capture the Problem when it's a 500/INTERNAL — RecipientPage renders
          // ErrorScreen(500) with the Ref line instead of the plain .rp-error markup. The generic 'error' status
          // is preserved so existing terminal classification (TRANSFER_EXPIRED → 'expired' etc.) is unchanged.
          const capturedProblem = problem?.code
            ? { code: problem.code, correlationId: problem.correlationId }
            : null
          set({ status: 'error', error: ERROR_MESSAGE, capturedProblem })
          return
        }
        const body = (await res.json()) as {
          from: string
          note: string | null
          files: RecipientFile[]
          hasDownloadAll: boolean
          downloadsLeft: number | null
          passwordRequired: boolean
          orgName?: string | null
          orgLogoUrl?: string | null
          scheduledSendAt?: string | null
          adsEnabled?: boolean
        }
        set({
          status: 'active',
          from: body.from,
          note: body.note,
          files: body.files,
          hasDownloadAll: body.hasDownloadAll,
          downloadsLeft: body.downloadsLeft,
          passwordRequired: body.passwordRequired,
          orgName: body.orgName ?? null,
          orgLogoUrl: body.orgLogoUrl ?? null,
          scheduledSendAt: body.scheduledSendAt ?? null,
          adsEnabled: body.adsEnabled === true,
          terminalMessage: null,
          error: null,
        })
      } catch (err) {
        // AbortError means "superseded / unmounted" — only the timeout abort flips to 'error'.
        if (err instanceof Error && err.name === 'AbortError') {
          if (timedOut) set({ status: 'error', error: ERROR_MESSAGE })
          return
        }
        set({ status: 'error', error: ERROR_MESSAGE })
      } finally {
        clearTimeout(timer)
      }
    })(),
  unlock: (linkId, password) =>
    (async () => {
      // Same abort/timeout pattern as load() — one transfer page at a time; the controller is shared, so an
      // unlock supersedes a load and vice versa.
      activeController?.abort()
      const controller = new AbortController()
      activeController = controller
      let timedOut = false
      const timer = setTimeout(() => { timedOut = true; controller.abort() }, LOAD_TIMEOUT_MS)
      set({ unlocking: true, unlockError: null }) // re-arms the shake — clears any previous wrong-password alert.
      try {
        const res = await fetch(UNLOCK_ENDPOINT.replace('{linkId}', linkId), {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
          body: JSON.stringify({ password }), // TA-4.2#5 exact wire shape — { password } only.
          signal: controller.signal,
        })
        if (res.ok) {
          const body = (await res.json()) as { token?: string } | null
          const token = typeof body?.token === 'string' ? body.token : ''
          if (token.length === 0) {
            // A 200 without a usable token is a server bug — generic error rather than a silent re-gate.
            set({ ...LOADING_SHAPE, status: 'error', error: ERROR_MESSAGE })
            return
          }
          // Tab-isolated token (EC-003-2 by design) — never a cookie, never localStorage.
          window.sessionStorage.setItem(tokenKey(linkId), token)
          await get().load(linkId) // re-queries endpoint 4 with ?t= — the file list renders without re-prompting.
          return
        }
        const problem = (await res.json()) as { code?: string; details?: string } | null
        const terminal = classifyProblem(problem)
        if (terminal !== null) {
          // T-003-03 precedence: terminal codes win over the gate — expired/limit screens replace the password card.
          set({ ...LOADING_SHAPE, status: terminal.status, terminalMessage: terminal.message })
          return
        }
        if (problem?.code === WRONG_PASSWORD_CODE) {
          // Stay active+gated; the page renders the shake + role=alert from unlockError. No lockout — retry stays live.
          set({
            unlocking: false,
            unlockError: typeof problem.details === 'string' && problem.details.length > 0
              ? problem.details
              : WRONG_PASSWORD_MESSAGE,
          })
          return
        }
        set({ unlocking: false, status: 'error', error: ERROR_MESSAGE })
      } catch (err) {
        // AbortError means "superseded / unmounted" — only the timeout abort flips to 'error' (same as load()).
        if (err instanceof Error && err.name === 'AbortError') {
          if (timedOut) set({ unlocking: false, status: 'error', error: ERROR_MESSAGE })
          return
        }
        set({ unlocking: false, status: 'error', error: ERROR_MESSAGE })
      } finally {
        clearTimeout(timer)
      }
    })(),
  reset: () => {
    activeController?.abort() // never let an aborted load's response land after unmount.
    activeController = null
    cancelDownloadAll() // T-004-03: no poll or response may write state after unmount.
    set({ ...LOADING_SHAPE })
  },
  dismissDownloadAllError: () => set({ downloadAllFailed: false }),
  downloadAll: (linkId) => {
    cancelDownloadAll()
    const startedAt = Date.now() // T-012-02: client-measured durationMs spans the whole preparing wait.
    let preparingStartedAt: number | null = null
    set({ downloadAllPhase: 'requesting', downloadAllFailed: false })
    const fail = () => set({ downloadAllPhase: 'idle', downloadAllFailed: true })
    const poll = async (): Promise<void> => {
      const controller = new AbortController()
      downloadAllController = controller
      let timedOut = false
      const timer = setTimeout(() => { timedOut = true; controller.abort() }, LOAD_TIMEOUT_MS)
      try {
        const token = readUnlockToken(linkId)
        const url = DOWNLOAD_ALL_URL_ENDPOINT.replace('{linkId}', linkId)
          + (token !== null ? `?t=${encodeURIComponent(token)}` : '')
        const res = await fetch(url, { signal: controller.signal, headers: { traceparent: getTraceParent() } })
        if (downloadAllController !== controller) return // reset()/superseded: ignore the response.
        if (res.status === 202) {
          const body = (await res.json().catch(() => null)) as { pollAfterSec?: number } | null
          if (downloadAllController !== controller) return
          const now = Date.now()
          preparingStartedAt ??= now
          if (now - preparingStartedAt >= ZIP_PREPARE_TIMEOUT_MS) { fail(); return }
          set({ downloadAllPhase: 'preparing' })
          const sec = typeof body?.pollAfterSec === 'number' && body.pollAfterSec > 0 ? body.pollAfterSec : DEFAULT_POLL_AFTER_SEC
          downloadAllTimer = setTimeout(() => { downloadAllTimer = null; void poll() }, sec * 1000)
          return
        }
        if (!res.ok) {
          const problem = (await res.json().catch(() => null)) as { code?: string; details?: string } | null
          if (downloadAllController !== controller) return
          const terminal = classifyProblem(problem)
          if (terminal !== null) {
            set({ ...LOADING_SHAPE, status: terminal.status, terminalMessage: terminal.message })
            return
          }
          fail() // 401 / 503 / junk - one failure copy; per-file rows stay usable (EC-004-4).
          return
        }
        const body = (await res.json()) as { url: string }
        if (downloadAllController !== controller) return
        // Same Azurite re-sign + same-tab navigation as per-file (AC-017-2 / EC-017-4 - a navigation, not a popup).
        const href = await browserDownloadUrl(body.url)
        if (downloadAllController !== controller) return
        window.location.href = href
        const { files, downloadsLeft } = get()
        trackClientEvent('download_completed', {
          fileId: null, // TA-7.2: fileId=null for download-all.
          bytes: files.reduce((sum, f) => sum + f.sizeBytes, 0),
          durationMs: Date.now() - startedAt,
        })
        // The server counted one zip download at mint time - decrement when the cap is finite.
        set({ downloadAllPhase: 'idle', ...(downloadsLeft !== null ? { downloadsLeft: downloadsLeft - 1 } : {}) })
        markCompleted(files.map((f) => f.fileId))
      } catch (err) {
        // AbortError means reset()/superseded - silent; the timeout abort is a real failure.
        if (err instanceof Error && err.name === 'AbortError') {
          if (timedOut) fail()
          return
        }
        fail()
      } finally {
        clearTimeout(timer)
      }
    }
    void poll()
  },
  download: (linkId, fileId) => {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), LOAD_TIMEOUT_MS)
    // T-012-02 (TA-10.2): client-measured durationMs — capture at the start of this download action.
    const downloadStartedAt = Date.now()
    set({ downloadingFileId: fileId, downloadErrorFileId: null }) // row indicator on; clears any previous per-row error
    ;(async () => {
      try {
        // T-003-05 (AC-003-2): endpoint 6 accepts the same-tab stored unlock token (?t=) — gated transfers download too.
        const token = readUnlockToken(linkId)
        const url = DOWNLOAD_URL_ENDPOINT
          .replace('{linkId}', linkId)
          .replace('{fileId}', fileId)
          + (token !== null ? `?t=${encodeURIComponent(token)}` : '')
        const res = await fetch(url, { signal: controller.signal, headers: { traceparent: getTraceParent() } })
        if (!res.ok) {
          const problem = (await res.json()) as { code?: string; details?: string } | null
          const terminal = classifyProblem(problem)
          if (terminal !== null) {
            // T-003-03 precedence: a terminal code (e.g. cap reached on this very mint) wins — the shared screen replaces the list.
            set({ ...LOADING_SHAPE, status: terminal.status, terminalMessage: terminal.message })
            return
          }
          // US-003-03 alt flow / F-TRF-013 MVP (503 + INTERNAL): storage degraded — row-level "File temporarily unavailable."
          set({ downloadingFileId: null, downloadErrorFileId: fileId })
          return
        }
        const body = (await res.json()) as { url: string; expiresAt: string | null }
        // T-016 (TA-4.2#6): the browser streams the blob directly against storage — no fetch-blob (up to ~5 GB of memory),
        // no download attribute (cross-origin navigation ignores it); Content-Disposition attachment does the naming.
        // T-003-08-fix (T-055-05/TA-14.3, StagingList precedent): re-address + protocol-re-sign for local Azurite before
        // navigating — the raw minted URL 404s against an IP-hosted account and spr=https rejects the plain-HTTP leg.
        window.location.href = await browserDownloadUrl(body.url)
        // T-012-02 (TA-10.2): download_completed — bytes from RecipientFile state, durationMs client-measured.
        const fileForTelemetry = get().files.find((f) => f.fileId === fileId)
        trackClientEvent('download_completed', {
          fileId,
          bytes: fileForTelemetry?.sizeBytes ?? 0,
          durationMs: Date.now() - downloadStartedAt,
        })
        // Exit check: "Downloads left" decrements by 1 when the cap is finite - the server counted at mint time.
        // T-004-03: the in-flight indicator clears unconditionally (unlimited caps used to leave per-file buttons disabled).
        const { downloadsLeft } = get()
        set({ downloadingFileId: null, ...(downloadsLeft !== null ? { downloadsLeft: downloadsLeft - 1 } : {}) })
        markCompleted([fileId])
      } catch (err) {
        // AbortError means unmounted/superseded — no row error for a cancelled page.
        if (!(err instanceof Error && err.name === 'AbortError')) {
          set({ downloadingFileId: null, downloadErrorFileId: fileId })
        }
      } finally {
        clearTimeout(timer)
      }
    })()
  },
}
})
