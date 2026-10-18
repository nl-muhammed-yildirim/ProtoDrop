import { create } from 'zustand'

/**
 * T-003-02 (TA-4.2#4): Zustand store for the recipient page at /t/{linkId} — holds endpoint 4's payload
 * (from, note, files, hasDownloadAll, downloadsLeft, passwordRequired) + status (loading/active/error).
 * load(linkId) fetches GET /api/v1/public/transfers/{linkId} with an AbortController + 30 s timeout; errors land in
 * state and never reject (UploadEngine convention), so the page view surfaces no unhandled rejection.
 * downloadsLeft / passwordRequired are stored but not rendered yet — T-003-03 and T-003-05 own their UI.
 */

export interface RecipientFile { fileId: string; name: string; sizeBytes: number }

export type RecipientStatus = 'loading' | 'active' | 'error'

// T-003-02 (TA-4.2#4): endpoint 4 — fixed-port style like LinkScreen/StagingList; page views mint no SAS, no auth, no cookie.
const PUBLIC_TRANSFER_ENDPOINT = 'http://localhost:8080/api/v1/public/transfers/{linkId}'
const LOAD_TIMEOUT_MS = 30_000
const ERROR_MESSAGE = 'Something went wrong'

interface RecipientState {
  status: RecipientStatus
  from: string
  note: string | null
  files: RecipientFile[]
  hasDownloadAll: boolean
  /** null = unlimited — the wire shape is held here now (TA-4.2#4); rendered in T-003-03. */
  downloadsLeft: number | null
  /** Gate UI is T-003-05; this field exists for the wire shape only. */
  passwordRequired: boolean
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
          // 404/410 Problem+JSON terminal states — T-003-03 builds the dedicated screens; generic error state for now.
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
