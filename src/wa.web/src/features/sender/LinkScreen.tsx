import { useEffect, useRef, useState } from 'react'

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
        {/* T-002-05…T-002-07: recipients/password/note fields */}
      </div>
    </section>
  )
}

export default LinkScreen
