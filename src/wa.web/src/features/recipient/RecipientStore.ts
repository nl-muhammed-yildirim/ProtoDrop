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
 * client copy exists only as a fallback when details is missing. downloadsLeft renders at T-003-03; passwordRequired
 * is gate UI for T-003-05.
 */

export interface RecipientFile { fileId: string; name: string; sizeBytes: number }

export type RecipientStatus = 'loading' | 'active' | 'error' | 'expired' | 'downloadLimit'

// T-003-02 (TA-4.2#4): endpoint 4 — fixed-port style like LinkScreen/StagingList; page views mint no SAS, no auth, no cookie.
const PUBLIC_TRANSFER_ENDPOINT = 'http://localhost:8080/api/v1/public/transfers/{linkId}'
const LOAD_TIMEOUT_MS = 30_000
const ERROR_MESSAGE = 'Something went wrong'

// T-003-03 (TA-4.1.3): endpoint 4's terminal Problem+JSON codes — the closed list pinned at wa.domain/Errors/ProblemCodes.cs.
// Literal client strings by design: the store never imports server constants; RecipientPage.test.tsx pins both, so a
// server rename surfaces in CI as a test failure instead of a silent generic-error fallback.
const TERMINAL_CODE_EXPIRED = 'TRANSFER_EXPIRED'
const TERMINAL_CODE_DOWNLOAD_LIMIT = 'MAX_DOWNLOADS_REACHED'

// Client-only fallback copy when the Problem+JSON omits details — server copy is authoritative (TA-4.1.3's string slot).
const TERMINAL_MESSAGES: Record<'expired' | 'downloadLimit', string> = {
  expired: 'This transfer has expired.',
  downloadLimit: 'All downloads have been used.',
}

interface RecipientState {
  status: RecipientStatus
  from: string
  note: string | null
  files: RecipientFile[]
  hasDownloadAll: boolean
  /** null = unlimited — the wire shape is held here (TA-4.2#4); rendered as "Downloads left: N" (T-003-03). */
  downloadsLeft: number | null
  /** Gate UI is T-003-05; this field exists for the wire shape only. */
  passwordRequired: boolean
  /** T-003-03: Problem+JSON details verbatim — the screen's single line on terminal screens (TA-4.1.3). */
  terminalMessage: string | null
  error: string | null
}

interface RecipientActions {
  load(linkId: string): Promise<void>
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
  error: null as string | null,
}

export const useRecipientStore = create<RecipientState & RecipientActions>((set) => ({
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
        const res = await fetch(PUBLIC_TRANSFER_ENDPOINT.replace('{linkId}', linkId), { signal: controller.signal })
        if (!res.ok) {
          // T-003-03 (FR-003-8/9): classify by the Problem+JSON code, not the HTTP status — 410 and 404 both map to
          // 'expired' when the code is TRANSFER_EXPIRED; a non-Problem body (or parse failure) keeps the generic error.
          const problem = (await res.json()) as { code?: string; details?: string } | null
          const terminalStatus: RecipientStatus | null =
            problem?.code === TERMINAL_CODE_EXPIRED ? 'expired'
            : problem?.code === TERMINAL_CODE_DOWNLOAD_LIMIT ? 'downloadLimit'
            : null
          if (terminalStatus !== null) {
            // Terminal states carry no payload — LOADING_SHAPE already reset every active field, so nothing leaks.
            const details = typeof problem?.details === 'string' && problem.details.length > 0
              ? problem.details
              : TERMINAL_MESSAGES[terminalStatus]
            set({ ...LOADING_SHAPE, status: terminalStatus, terminalMessage: details })
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
  reset: () => {
    activeController?.abort() // never let an aborted load's response land after unmount.
    activeController = null
    set({ ...LOADING_SHAPE })
  },
}))
