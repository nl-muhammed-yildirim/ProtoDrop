import { useEffect, useRef, useState } from 'react'

import { useUploadEngine } from '../../core/upload/UploadEngine'
import { getTraceParent } from '../../core/traceparent'
import { MAX_EMAILS, parseRecipients, validateAddress } from './recipients'
import i18n from '../../core/i18n/init'

interface LinkScreenProps {
  linkId: string
  senderName?: string
  transferId: string
  onSendAgain?: (entries: Array<{ name: string; size: number }>) => void
  // T-010-03 (FR-010-2/4): re-send pre-fill props — the screen is reused for the re-send flow.
  initialRecipients?: string
  initialNote?: string
  passwordRequired?: boolean
  sentHeading?: string
  bannerFiles?: { firstFile: string; count: number } | null
}

type SendResponse = {
  id: string
  linkId: string
  status: string
  expiresAt: string
  totalBytes: number
  fileCount: number
  recipients: string[]
}

type SendPhase = 'form' | 'sent'

// T-002-04 (TA-4.2#3): endpoint 3 — activate the Draft transfer; {id} filled per component (same fixed-port style as StagingList's DRAFT_ENDPOINT).
const SEND_ENDPOINT = 'http://localhost:8080/api/v1/transfers/{id}/send'

/**
 * T-002-03: Link screen — the hero card shown after finalize.
 * Displays the full public URL `{origin}/t/{linkId}` in a read-only copy field
 * with a Copy button (navigator.clipboard + execCommand fallback).
 * Success state shows "Copied ✓" in --success for 2 s, then reverts (UI-Reference §4.5).
 * T-002-08: "Copy link only" + confirmation screen (FR-002-6/7, AC-002-3, TA-4.2#3): zero-email send via endpoint 3 with an
 * Idempotency-Key; whitespace-only recipients follow the same path; after any successful send the card swaps to
 * "Transfer sent." — link + Copy + Send again (M0 client-side pre-fill; F-TRF-010's endpoint 16 later).
 */
export function LinkScreen({ linkId, senderName, transferId, onSendAgain, initialRecipients, initialNote, passwordRequired, sentHeading, bannerFiles }: LinkScreenProps) {
  const [copied, setCopied] = useState(false)
  const timeoutRef = useRef<number | null>(null)

  // T-010-03 (AC-010-2 client half): pre-fill from the original transfer's snapshot — editable; the send body carries the form values.
  const [recipientsRaw, setRecipientsRaw] = useState(initialRecipients ?? '')
  const [invalidAddresses, setInvalidAddresses] = useState<string[]>([])
  const [capExceeded, setCapExceeded] = useState(false)

  // T-002-07: sender name, email, and note fields
  const [fromName, setFromName] = useState(senderName ?? '')
  const [senderEmail, setSenderEmail] = useState('')
  const [emailError, setEmailError] = useState(false)
  const [note, setNote] = useState(initialNote ?? '')
  const senderEmailRef = useRef(senderEmail)

  // T-010-03 (FR-010-2): password field — starts empty in all cases (PBKDF2 is one-way; the plaintext can't be recovered).
  const [passwordValue, setPasswordValue] = useState('')
  const [showPassword, setShowPassword] = useState(false)

  // T-002-08: send + confirmation state (FR-002-6/7) — phase only; the SendResponse body is intentionally not stored.
  const [sendState, setSendState] = useState<SendPhase>('form')
  const [sending, setSending] = useState(false) // double-click guard — endpoint 3 is idempotent per key anyway (TA-4.1.5).
  const [sendError, setSendError] = useState<string | null>(null)

  const url = `${window.location.origin}/t/${linkId}`

  useEffect(() => {
    return () => {
      if (timeoutRef.current !== null) window.clearTimeout(timeoutRef.current)
    }
  }, [])

  const handleCopy = async (): Promise<void> => {
    let ok = false
    try {
      await navigator.clipboard.writeText(url)
      ok = true
    } catch {
      // Fallback: temporary textarea + execCommand (non-secure contexts).
      const ta = document.createElement('textarea')
      ta.value = url
      ta.style.position = 'fixed'
      ta.style.opacity = '0'
      document.body.appendChild(ta)
      ta.select()
      try {
        ok = document.execCommand('copy')
      } finally {
        document.body.removeChild(ta)
      }
    }
    if (ok) {
      setCopied(true)
      timeoutRef.current = window.setTimeout(() => setCopied(false), 2000)
    }
  }

  const validateRecipients = (raw: string): void => {
    const parsed = parseRecipients(raw)
    const invalid = parsed.filter((addr) => !validateAddress(addr))
    setInvalidAddresses(invalid)
    setCapExceeded(parsed.length > MAX_EMAILS)
  }

  const handleRecipientsChange = (event: React.ChangeEvent<HTMLTextAreaElement>): void => {
    const value = event.target.value
    setRecipientsRaw(value)
    validateRecipients(value)
  }

  const handleRecipientsBlur = (): void => {
    validateRecipients(recipientsRaw)
  }

  const handleEmailChange = (event: React.ChangeEvent<HTMLInputElement>): void => {
    setSenderEmail(event.target.value)
    senderEmailRef.current = event.target.value
    setEmailError(false)
  }

  const handleEmailBlur = (): void => {
    // Read the DOM value directly — robust against tests that bypass React state via native setters.
    const input = document.getElementById('sender-email') as HTMLInputElement | null
    const value = (input?.value ?? '').trim()
    if (value.length === 0) {
      setEmailError(false)
    } else {
      setEmailError(!validateAddress(value))
    }
  }

  // React 18's synthetic blur handler can be swallowed by jsdom when the event is dispatched
  // directly on the element (instead of bubbling through the root). A native listener guarantees
  // the validation runs in both real browsers and tests.
  useEffect(() => {
    const input = document.getElementById('sender-email') as HTMLInputElement | null
    if (!input) return

    const onBlur = (): void => {
      handleEmailBlur()
    }

    input.addEventListener('blur', onBlur)
    return () => {
      input.removeEventListener('blur', onBlur)
    }
  }, [])

  const noteTooLong = note.length > 500

  // T-002-08: shared blocking reason — invalid address or cap exceeded; wrapper spans' title explains why (§4.2).
  const blockReason: string | null =
    capExceeded ? i18n.t('link.ready.maxRecipients', { max: MAX_EMAILS }) : invalidAddresses.length > 0 ? i18n.t('link.ready.fixInvalid') : null

  const blankOr = (value: string): string | undefined => (value.trim() === '' ? undefined : value) // blank sender fields are omitted.

  // T-002-08 (AC-002-3): endpoint 3 — activate the transfer; emails [] is the link-only path (zero EmailRecipient rows).
  const sendTransfer = async (emails: string[]): Promise<boolean> => {
    setSending(true)
    setSendError(null)
    try {
      let res: Response
      try {
        res = await fetch(SEND_ENDPOINT.replace('{id}', transferId), {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Idempotency-Key': crypto.randomUUID(),
            traceparent: getTraceParent(),
          },
          body: JSON.stringify({
            emails,
            senderName: blankOr(fromName),
            senderEmail: blankOr(senderEmail),
            note: blankOr(note),
            // T-010-03 (FR-010-2): password — only included when non-empty; the handler at Program.cs L217 reads it.
            password: blankOr(passwordValue),
          }),
        })
      } catch {
        // fetch() rejection (network down / CORS / dev server not up on :8080) — same inline error as the non-ok path.
        setSendError('Sending failed. Please try again.')
        return false
      }
      if (!res.ok) {
        setSendError('Sending failed. Please try again.')
        return false
      }
      const response = (await res.json()) as SendResponse
      void response // T-002-08: phase-only state — the body is cast for the record but intentionally never read.
      setSendState('sent')
      return true
    } finally {
      setSending(false)
    }
  }

  // T-002-08 (FR-002-6): "Copy link only" — copy + activate with zero emails in one click.
  const handleCopyLinkOnly = async (): Promise<void> => {
    if (sending || blockReason !== null) return
    await handleCopy()
    // Whitespace-only recipients ⇒ [] — the same link-only path.
    await sendTransfer(parseRecipients(recipientsRaw))
  }

  // T-002-08: "Send transfer" — non-empty parsed emails from the field.
  const handleSendTransfer = async (): Promise<void> => {
    if (sending || blockReason !== null) return
    await sendTransfer(parseRecipients(recipientsRaw))
  }

  return (
    <section className="ls" aria-label={sendState === 'sent' ? i18n.t('link.ready.transferSent') : i18n.t('link.ready.linkReady')}>
      <div className="ls-card">
        {sendState === 'sent' ? (
          // T-002-08 (FR-002-7): confirmation screen — in-place swap, no route navigation.
          <>
            <h1 className="ls-heading">{sentHeading ?? i18n.t('link.ready.transferSent')}</h1>
            <p className="ls-sent-sub">{i18n.t('link.ready.shareLater')}</p>
            <div className="ls-field">
              <span className="ls-url">{url}</span>
              <button type="button" className="ls-copy-btn" onClick={() => void handleCopy()}>
                {copied ? i18n.t('link.ready.copied') : i18n.t('link.ready.copy')}
              </button>
            </div>
            {copied && (
              <p className="ls-copy-success" role="status">{i18n.t('link.ready.copied')}</p>
            )}
            {/* M0: client-side pre-fill via onSendAgain — F-TRF-010's endpoint 16 does the re-draft later. */}
            <button
              type="button"
              className="ls-send-btn"
              onClick={() => onSendAgain?.(useUploadEngine.getState().files.map((f) => ({ name: f.name, size: f.size })))}
            >
              {i18n.t('link.ready.sendAgain')}
            </button>
          </>
        ) : (
          <>
            <h1 className="ls-heading">{i18n.t('link.ready.linkReady')}</h1>
            {/* T-010-03 (EC-010-3): re-send banner — first file name + "and N more files" (all-or-nothing; no per-file selection). */}
            {bannerFiles && (
              <p className="ls-banner">
                {bannerFiles.count === 1
                  ? i18n.t('link.ready.resendSingle', { file: bannerFiles.firstFile })
                  : i18n.t('link.ready.resendMany', { file: bannerFiles.firstFile, more: bannerFiles.count - 1 })}
              </p>
            )}
            <div className="ls-field">
              <span className="ls-url">{url}</span>
              <button type="button" className="ls-copy-btn" onClick={() => void handleCopy()}>
                {copied ? i18n.t('link.ready.copied') : i18n.t('link.ready.copy')}
              </button>
            </div>
            {copied && (
              <p className="ls-copy-success" role="status">{i18n.t('link.ready.copied')}</p>
            )}
            {/* T-002-05: recipients field — parse + validate on blur; per-address inline errors, no blocking modal */}
            <div className="ls-recipients">
              <label className="ls-recipients-label" htmlFor="recipients-textarea">{i18n.t('link.ready.recipientsLabel')}</label>
              <textarea
                id="recipients-textarea"
                className="ls-recipients-textarea"
                value={recipientsRaw}
                onChange={handleRecipientsChange}
                onBlur={handleRecipientsBlur}
                placeholder={i18n.t('link.ready.recipientsPlaceholder')}
              />
              <p className="ls-recipients-helper">{i18n.t('link.ready.helperText')}</p>
              {invalidAddresses.map((addr) => (
                <p key={addr} className="ls-recipients-error">{addr}</p>
              ))}
              {capExceeded && (
                <p className="ls-recipients-error">{i18n.t('link.ready.maxRecipients', { max: MAX_EMAILS })}</p>
              )}
            </div>
            {/* T-002-07: sender name, email, and note fields */}
            <div className="ls-sender">
              <label className="ls-sender-label" htmlFor="from-name">{i18n.t('link.ready.fromLabel')}</label>
              <input
                id="from-name"
                type="text"
                className="ls-sender-input"
                value={fromName}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setFromName(e.target.value)}
                maxLength={100}
                placeholder={i18n.t('link.ready.yourNamePlaceholder')}
              />
            </div>
            <div className="ls-sender">
              <label className="ls-sender-label" htmlFor="sender-email">{i18n.t('link.ready.emailLabel')}</label>
              <input
                id="sender-email"
                type="email"
                className="ls-sender-input"
                value={senderEmail}
                onChange={handleEmailChange}
                onBlur={handleEmailBlur}
                placeholder={i18n.t('link.ready.emailPlaceholder')}
              />
              {emailError && (
                <p className="ls-sender-error">{i18n.t('link.ready.emailError')}</p>
              )}
            </div>
            <div className="ls-sender">
              <label className="ls-sender-label" htmlFor="note-textarea">{i18n.t('link.ready.noteLabel')}</label>
              <textarea
                id="note-textarea"
                className="ls-note-textarea"
                value={note}
                onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) => setNote(e.target.value)}
                maxLength={500}
                placeholder={i18n.t('link.ready.addNotePlaceholder')}
              />
              <span className={`ls-char-counter${note.length >= 450 ? ' ls-char-counter--warning' : ''}`}>
                {`${note.length}/500`}
              </span>
              {noteTooLong && (
                <p className="ls-sender-error">{i18n.t('link.ready.noteTooLong')}</p>
              )}
            </div>
            {/* T-010-03 (FR-010-2, UI-Reference §5.2): password field — starts empty; the label notes a password was set on the original. */}
            <div className="ls-password-field">
              <label className="ls-sender-label" htmlFor="link-password">
                {i18n.t('link.ready.passwordLabel')}{passwordRequired === true ? i18n.t('link.ready.passwordSuffix') : ''}
              </label>
              <div className="ls-password-row">
                <input
                  id="link-password"
                  type={showPassword ? 'text' : 'password'}
                  className="ls-password-input"
                  value={passwordValue}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setPasswordValue(e.target.value)}
                  placeholder={i18n.t('link.ready.optionalPlaceholder')}
                  autoComplete="new-password"
                />
                <button
                  type="button"
                  className="ls-password-toggle"
                  aria-label={showPassword ? i18n.t('link.ready.hidePasswordAria') : i18n.t('link.ready.showPasswordAria')}
                  aria-pressed={showPassword}
                  onClick={() => setShowPassword((s) => !s)}
                >
                  {showPassword ? i18n.t('link.ready.hideButton') : i18n.t('link.ready.showButton')}
                </button>
              </div>
            </div>
            {/* T-002-08: Send transfer + Copy link only — disabled while blocked or in flight; wrapper spans carry the title (§4.2). */}
            <div className="ls-actions">
              <span className="ls-actions-wrap" title={blockReason ?? undefined}>
                <button type="button" className="ls-send-btn" onClick={() => void handleSendTransfer()} disabled={sending || blockReason !== null}>{i18n.t('link.ready.sendTransferButton')}</button>
              </span>
              <span className="ls-actions-wrap" title={blockReason ?? undefined}>
                <button type="button" className="ls-copyonly-btn" onClick={() => void handleCopyLinkOnly()} disabled={sending || blockReason !== null}>{i18n.t('link.ready.copyLinkOnlyButton')}</button>
              </span>
            </div>
            {sendError && (
              <p className="ls-send-error" role="alert">{sendError}</p>
            )}
          </>
        )}
      </div>
    </section>
  )
}

export default LinkScreen
