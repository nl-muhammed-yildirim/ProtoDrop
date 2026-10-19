import { create } from 'zustand'

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
 */

export interface RecipientFile { fileId: string; name: string; sizeBytes: number }

export type RecipientStatus = 'loading' | 'active' | 'error' | 'expired' | 'downloadLimit'

// T-003-02 (TA-4.2#4): endpoint 4 — fixed-port style like LinkScreen/StagingList; page views mint no SAS, no auth, no cookie.
const PUBLIC_TRANSFER_ENDPOINT = 'http://localhost:8080/api/v1/public/transfers/{linkId}'
// T-003-05 (TA-4.2#5): endpoint 5 — the only client-side consumer of the unlock token; no auth, no cookie, no SAS.
const UNLOCK_ENDPOINT = 'http://localhost:8080/api/v1/public/transfers/{linkId}/unlock'
// T-003-06 (TA-4.2#6): endpoint 6 — mint a 30-minute read SAS; the browser streams the blob directly against storage.
const DOWNLOAD_URL_ENDPOINT = 'http://localhost:8080/api/v1/public/transfers/{linkId}/files/{fileId}/download-url'
const LOAD_TIMEOUT_MS = 30_000
const ERROR_MESSAGE = 'Something went wrong'

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
  error: string | null
}

interface RecipientActions {
  load(linkId: string): Promise<void>
  /** T-003-05 (AC-003-2): POST endpoint 5 with the password; success stores the token and re-queries endpoint 4. */
  unlock(linkId: string, password: string): Promise<void>
  /** T-003-06: GET endpoint 6 with the stored ?t= token → browser navigates to the minted SAS (streams directly from storage). */
  download(linkId: string, fileId: string): void
  /** Aborts the in-flight load and restores the loading shape — keeps renders hermetic (UploadEngine reset style). */
  reset(): void
}

// One transfer page at a time; a new load (or reset) aborts the previous request.
let activeController: AbortController | null = null

const LOADING_SHAPE = {
  status: 'loading' as RecipientStatus,
  from: '',
  note: null as string | null,
  files: [] as RecipientFile[],
  hasDownloadAll: false,
  downloadsLeft: null as number | null,
  passwordRequired: false,
  terminalMessage: null as string | null,
  unlocking: false,
  unlockError: null as string | null,
  downloadingFileId: null as string | null,
  downloadErrorFileId: null as string | null,
  error: null as string | null,
}

export const useRecipientStore = create<RecipientState & RecipientActions>((set, get) => ({
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
        const res = await fetch(url, { signal: controller.signal })
        if (!res.ok) {
          // T-003-03 (FR-003-8/9): classify by the Problem+JSON code, not the HTTP status — 410 and 404 both map to
          // 'expired' when the code is TRANSFER_EXPIRED; a non-Problem body (or parse failure) keeps the generic error.
          const problem = (await res.json()) as { code?: string; details?: string } | null
          const terminal = classifyProblem(problem)
          if (terminal !== null) {
            // Terminal states carry no payload — LOADING_SHAPE already reset every active field, so nothing leaks.
            set({ ...LOADING_SHAPE, status: terminal.status, terminalMessage: terminal.message })
            return
          }
          set({ status: 'error', error: ERROR_MESSAGE })
          return
        }
        const body = (await res.json()) as {
          from: string
          note: string | null
          files: RecipientFile[]
          hasDownloadAll: boolean
          downloadsLeft: number | null
          passwordRequired: boolean
        }
        set({
          status: 'active',
          from: body.from,
          note: body.note,
          files: body.files,
          hasDownloadAll: body.hasDownloadAll,
          downloadsLeft: body.downloadsLeft,
          passwordRequired: body.passwordRequired,
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
          headers: { 'Content-Type': 'application/json' },
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
    set({ ...LOADING_SHAPE })
  },
  download: (linkId, fileId) => {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), LOAD_TIMEOUT_MS)
    set({ downloadingFileId: fileId, downloadErrorFileId: null }) // row indicator on; clears any previous per-row error
    ;(async () => {
      try {
        // T-003-05 (AC-003-2): endpoint 6 accepts the same-tab stored unlock token (?t=) — gated transfers download too.
        const token = readUnlockToken(linkId)
        const url = DOWNLOAD_URL_ENDPOINT
          .replace('{linkId}', linkId)
          .replace('{fileId}', fileId)
          + (token !== null ? `?t=${encodeURIComponent(token)}` : '')
        const res = await fetch(url, { signal: controller.signal })
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
        window.location.href = body.url
        // Exit check: "Downloads left" decrements by 1 when the cap is finite — the server counted at mint time.
        const { downloadsLeft } = get()
        if (downloadsLeft !== null) set({ downloadingFileId: null, downloadsLeft: downloadsLeft - 1 })
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
}))
