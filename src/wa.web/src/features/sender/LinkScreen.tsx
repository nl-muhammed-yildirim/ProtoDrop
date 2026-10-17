import { useEffect, useRef, useState } from 'react'

import { MAX_EMAILS, parseRecipients, validateAddress } from './recipients'

interface LinkScreenProps {
  linkId: string
}

/**
 * T-002-03: Link screen — the hero card shown after finalize.
 * Displays the full public URL `{origin}/t/{linkId}` in a read-only copy field
 * with a Copy button (navigator.clipboard + execCommand fallback).
 * Success state shows "Copied ✓" in --success for 2 s, then reverts (UI-Reference §4.5).
 */
export function LinkScreen({ linkId }: LinkScreenProps) {
  const [copied, setCopied] = useState(false)
  const timeoutRef = useRef<number | null>(null)

  const [recipientsRaw, setRecipientsRaw] = useState('')
  const [invalidAddresses, setInvalidAddresses] = useState<string[]>([])
  const [capExceeded, setCapExceeded] = useState(false)

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
        {/* T-002-06/T-002-07: password/note fields */}
      </div>
    </section>
  )
}

export default LinkScreen
