import { useEffect, useState } from 'react'

import LinkScreen from './LinkScreen'

interface ReSendScreenProps {
  originalTransferId: string
  onUpload: () => void
}

// T-010-03 (endpoint 16): ResendTransferResult wire shape — mirrors Program.cs L254-268 exactly (id in 'n'-form, status as "Draft").
type ResendTransferResult = {
  id: string
  linkId: string
  status: string
  expiresAt: string
  totalBytes: number
  fileCount: number
  files: Array<{ fileId: string; name: string }>
  recipients: string[]
  note?: string | null
  senderName: string
  senderEmail?: string | null
  passwordRequired: boolean
}

// T-010-03 (FR-010-4): endpoint 16 — POST /api/v1/transfers/{id}/resend (same fixed-port style as existing endpoints).
const RESEND_ENDPOINT = 'http://localhost:8080/api/v1/transfers/{id}/resend'

type ReSendState =
  | { phase: 'loading' }
  | { phase: 'filesGone' }
  | { phase: 'error' }
  | { phase: 'ready'; result: ResendTransferResult }

/**
 * T-010-03 (FR-010-2/4, AC-010-2 client half, EC-010-3): the re-send screen at /resend/{originalTransferId}.
 * On mount: POST endpoint 16 with cookie credentials. States: loading → filesGone (410 + code FILES_GONE — a
 * single-screen terminal state, no half-filled form) | error (non-410 failure — inline message + retry) |
 * ready (LinkScreen with the original's pre-filled values, all editable). The linkId/transferId passed to
 * LinkScreen are the NEW draft's minted values (endpoint 16 L68-74), not the original's.
 */
export function ReSendScreen({ originalTransferId, onUpload }: ReSendScreenProps) {
  const [state, setState] = useState<ReSendState>({ phase: 'loading' })
  // T-010-03: retry — bumping the key re-runs the mount effect (same pattern as RecipientPage's ?t= re-query).
  const [loadKey, setLoadKey] = useState(0)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const res = await fetch(RESEND_ENDPOINT.replace('{id}', originalTransferId), {
          method: 'POST',
          credentials: 'include',
        })
        if (res.status === 410) {
          // T-010-03 (FR-010-4): FILES_GONE — Problem+JSON body with code === "FILES_GONE" → single-screen state.
          const body = (await res.json().catch(() => null)) as { code?: string } | null
          if (!cancelled && body?.code === 'FILES_GONE') {
            setState({ phase: 'filesGone' })
            return
          }
        }
        if (!res.ok) {
          if (!cancelled) setState({ phase: 'error' })
          return
        }
        const result = (await res.json()) as ResendTransferResult
        if (!cancelled) setState({ phase: 'ready', result })
      } catch {
        if (!cancelled) setState({ phase: 'error' })
      }
    })()
    return () => {
      cancelled = true
    }
  }, [originalTransferId, loadKey])

  if (state.phase === 'loading') {
    return <p className="rs-loading" role="status">Loading…</p>
  }

  if (state.phase === 'filesGone') {
    // T-010-03 (EC-010-3): FILES_GONE is a screen-level state — no LinkScreen, no form, no error toast.
    return (
      <section className="rs-filesgone" aria-label="Files were deleted">
        <h1 className="rs-filesgone-heading">Files were deleted — upload again.</h1>
        <button type="button" className="btn-primary rs-filesgone-upload" onClick={onUpload}>Upload</button>
      </section>
    )
  }

  if (state.phase === 'error') {
    // T-010-03: generic error — non-410 failure → inline message + retry (same pattern as LinkScreen's sendError).
    return (
      <section className="rs-error" aria-label="Failed to load re-send details">
        <p role="alert" className="rs-error-message">Failed to load re-send details. Please try again.</p>
        <button type="button" className="btn-primary rs-error-retry" onClick={() => { setState({ phase: 'loading' }); setLoadKey((k) => k + 1) }}>Try again</button>
      </section>
    )
  }

  const { result } = state
  return (
    <LinkScreen
      linkId={result.linkId}
      transferId={result.id}
      senderName={result.senderName}
      initialRecipients={result.recipients.join('\n')}
      initialNote={result.note ?? ''}
      passwordRequired={result.passwordRequired}
      sentHeading="New link ready."
      bannerFiles={{ firstFile: result.files[0].name, count: result.files.length }}
    />
  )
}

export default ReSendScreen
