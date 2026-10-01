import { useEffect, useRef, useState } from 'react';
import { getPublicLink, useTransferStore } from '../../core/upload/TransferStore';

// US-002-01: Link screen — shows the public URL with a copy button (UI-Reference §5.2).
// Renders when transfer status is 'ready' (AC-002-1).

const COPY_RESET_MS = 2000;

export function LinkScreen() {
  const linkId = useTransferStore((state) => state.linkId);
  const [copied, setCopied] = useState(false);
  const timeoutRef = useRef<number | null>(null);

  // US-002-01: clean up the copy-reset timer on unmount.
  useEffect(() => {
    return () => {
      if (timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
      }
    };
  }, []);

  if (!linkId) return null;

  const publicUrl = getPublicLink(linkId);

  const handleCopy = async (): Promise<void> => {
    let success = false;
    try {
      await navigator.clipboard.writeText(publicUrl);
      success = true;
    } catch {
      // Fallback for non-secure contexts or older browsers.
      const tempInput = document.createElement('input');
      tempInput.value = publicUrl;
      tempInput.style.position = 'absolute';
      tempInput.style.left = '-9999px';
      document.body.appendChild(tempInput);
      tempInput.select();
      // Fix 9: check execCommand return value to avoid false-positive "Copied" feedback.
      success = document.execCommand('copy');
      document.body.removeChild(tempInput);
    }

    if (!success) return;
    setCopied(true);
    if (timeoutRef.current !== null) {
      window.clearTimeout(timeoutRef.current);
    }
    timeoutRef.current = window.setTimeout(() => {
      setCopied(false);
      timeoutRef.current = null;
    }, COPY_RESET_MS);
  };

  return (
    <div className="link-screen">
      <h2>Your link is ready</h2>
      <div className="link-copy-field">
        <input
          type="text"
          readOnly
          value={publicUrl}
          className="link-input"
          aria-label="Transfer link"
        />
        <button
          type="button"
          className={`btn btn-primary link-copy-btn ${copied ? 'link-copy-btn--success' : ''}`}
          onClick={handleCopy}
          aria-label={copied ? 'Copied' : 'Copy link'}
        >
          {copied ? 'Copied ✓' : 'Copy'}
        </button>
      </div>
    </div>
  );
}
