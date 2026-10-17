import { useEffect, useRef, useState } from 'react'

import { MAX_EMAILS, parseRecipients, validateAddress } from './recipients'

interface LinkScreenProps {
  linkId: string
  senderName?: string
}

/**
 * T-002-03: Link screen — the hero card shown after finalize.
 * Displays the full public URL `{origin}/t/{linkId}` in a read-only copy field
 * with a Copy button (navigator.clipboard + execCommand fallback).
 * Success state shows "Copied ✓" in --success for 2 s, then reverts (UI-Reference §4.5).
 */
export function LinkScreen({ linkId, senderName }: LinkScreenProps) {
  const [copied, setCopied] = useState(false)
  const timeoutRef = useRef<number | null>(null)

  const [recipientsRaw, setRecipientsRaw] = useState('')
  const [invalidAddresses, setInvalidAddresses] = useState<string[]>([])
  const [capExceeded, setCapExceeded] = useState(false)

  // T-002-07: sender name, email, and note fields
  const [fromName, setFromName] = useState(senderName ?? '')
  const [senderEmail, setSenderEmail] = useState('')
  const [emailError, setEmailError] = useState(false)
  const [note, setNote] = useState('')
  const senderEmailRef = useRef(senderEmail)

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

  return (
    <section className="ls" aria-label="Your link is ready">
      <div className="ls-card">
        <h1 className="ls-heading">Your link is ready</h1>
        <div className="ls-field">
          <span className="ls-url">{url}</span>
          <button type="button" className="ls-copy-btn" onClick={() => void handleCopy()}>
            {copied ? 'Copied ✓' : 'Copy'}
          </button>
        </div>
        {copied && (
          <p className="ls-copy-success" role="status">Copied ✓</p>
        )}
        {/* T-002-05: recipients field — parse + validate on blur; per-address inline errors, no blocking modal */}
        <div className="ls-recipients">
          <label className="ls-recipients-label" htmlFor="recipients-textarea">Recipients</label>
          <textarea
            id="recipients-textarea"
            className="ls-recipients-textarea"
            value={recipientsRaw}
            onChange={handleRecipientsChange}
            onBlur={handleRecipientsBlur}
            placeholder="one@address.com"
          />
          <p className="ls-recipients-helper">One per line — you can also separate with commas.</p>
          {invalidAddresses.map((addr) => (
            <p key={addr} className="ls-recipients-error">{addr}</p>
          ))}
          {capExceeded && (
            <p className="ls-recipients-error">Maximum {MAX_EMAILS} recipients</p>
          )}
        </div>
        {/* T-002-07: sender name, email, and note fields */}
        <div className="ls-sender">
          <label className="ls-sender-label" htmlFor="from-name">From</label>
          <input
            id="from-name"
            type="text"
            className="ls-sender-input"
            value={fromName}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setFromName(e.target.value)}
            maxLength={100}
            placeholder="Your name"
          />
        </div>
        <div className="ls-sender">
          <label className="ls-sender-label" htmlFor="sender-email">Your email (optional)</label>
          <input
            id="sender-email"
            type="email"
            className="ls-sender-input"
            value={senderEmail}
            onChange={handleEmailChange}
            onBlur={handleEmailBlur}
            placeholder="you@example.com"
          />
          {emailError && (
            <p className="ls-sender-error">Please enter a valid email address</p>
          )}
        </div>
        <div className="ls-sender">
          <label className="ls-sender-label" htmlFor="note-textarea">Note (optional)</label>
          <textarea
            id="note-textarea"
            className="ls-note-textarea"
            value={note}
            onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) => setNote(e.target.value)}
            maxLength={500}
            placeholder="Add a note for the recipient…"
          />
          <span className={`ls-char-counter${note.length >= 450 ? ' ls-char-counter--warning' : ''}`}>
            {note.length}/500
          </span>
          {noteTooLong && (
            <p className="ls-sender-error">Note is too long</p>
          )}
        </div>
      </div>
    </section>
  )
}

export default LinkScreen
