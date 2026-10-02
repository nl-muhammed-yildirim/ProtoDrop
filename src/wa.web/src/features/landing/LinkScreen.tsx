import { useEffect, useRef, useState } from 'react';
import { getPublicLink, useTransferStore } from '../../core/upload/TransferStore';
import { MAX_EMAILS, parseRecipients } from '../../core/upload/emailUtils';
import { showToast } from '../../core/ui/Toast';

// US-002-01: Link screen — shows the public URL with a copy button (UI-Reference §5.2).
// Renders when transfer status is 'ready' (AC-002-1).

const COPY_RESET_MS = 2000;

export function LinkScreen() {
  const linkId = useTransferStore((state) => state.linkId);
  const sendStatus = useTransferStore((state) => state.sendStatus);
  const recipients = useTransferStore((state) => state.recipients);
  const sendError = useTransferStore((state) => state.sendError);
  const send = useTransferStore((state) => state.send);
  const resetSend = useTransferStore((state) => state.resetSend);
  const [copied, setCopied] = useState(false);
  const [localText, setLocalText] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);
  // US-002-03: optional password field (sender-side).
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const timeoutRef = useRef<number | null>(null);
  // US-002-02: guard against double-click before re-render disables the button.
  const isSendingRef = useRef(false);

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

  const validate = (): string | null => {
    const { valid, invalid } = parseRecipients(localText);
    if (invalid.length > 0) return `Invalid: ${invalid.join(', ')}`;
    if (valid.length === 0) return 'Enter at least one recipient.';
    if (valid.length > MAX_EMAILS) return `Maximum ${MAX_EMAILS} recipients.`;
    return null;
  };

  const handleBlur = (): void => {
    setValidationError(validate());
  };

  const handleSend = async (): Promise<void> => {
    // US-002-02: prevent double-click before re-render disables the button.
    if (isSendingRef.current) return;
    isSendingRef.current = true;

    try {
      const { valid, invalid } = parseRecipients(localText);

      // Set inline error for UI feedback, but don't block send if there are valid addresses.
      if (invalid.length > 0) {
        setValidationError(`Invalid: ${invalid.join(', ')}`);
      } else {
        setValidationError(null);
      }

      if (valid.length === 0) {
        setValidationError('Enter at least one recipient.');
        return;
      }
      if (valid.length > MAX_EMAILS) {
        setValidationError(`Maximum ${MAX_EMAILS} recipients.`);
        return;
      }

      // US-002-03: pass password only when non-empty.
      const pw = password.length > 0 ? password : undefined;
      await send(linkId, valid, `send-${linkId}`, pw);
      if (useTransferStore.getState().sendStatus === 'idle') {
        showToast('error', useTransferStore.getState().sendError ?? 'Send failed.');
      }
    } finally {
      isSendingRef.current = false;
    }
  };

  const handleSendAgain = (): void => {
    resetSend();
    setLocalText('');
    setValidationError(null);
    setPassword('');
    setShowPassword(false);
  };

  return (
    <div className="link-screen">
      {sendStatus === 'sent' ? (
        <>
          <h2>Sent to {recipients.length} recipient{recipients.length === 1 ? '' : 's'}</h2>
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
          <button
            type="button"
            className="btn btn-secondary send-again-btn"
            onClick={handleSendAgain}
          >
            Send again
          </button>
        </>
      ) : (
        <>
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
          <div className="recipients-section">
            <label className="recipients-label" htmlFor="recipients-input">
              Recipients
            </label>
            <textarea
              id="recipients-input"
              className="recipients-textarea"
              rows={3}
              placeholder="one@x.com, two@y.com"
              value={localText}
              onChange={(event) => setLocalText(event.target.value)}
              onBlur={handleBlur}
            />
            <p className="recipients-helper">One per line — you can also separate with commas.</p>
            {(validationError || sendError) && (
              <p role="alert" aria-live="polite" className="recipients-error">
                {validationError ?? sendError}
              </p>
            )}
            {/* US-002-03: optional password field. */}
            <label className="recipients-label" htmlFor="password-input">
              Password
            </label>
            <div className="password-field">
              <input
                id="password-input"
                type={showPassword ? 'text' : 'password'}
                className="password-input"
                aria-label="Password"
                maxLength={64}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
              <button
                type="button"
                className="password-toggle"
                aria-controls="password-input"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                onClick={() => setShowPassword((prev) => !prev)}
              >
                {showPassword ? (
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
                    <line x1="1" y1="1" x2="23" y2="23" />
                  </svg>
                ) : (
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                    <circle cx="12" cy="12" r="3" />
                  </svg>
                )}
              </button>
            </div>
            <p className="recipients-helper">Optional — only people with this password can download.</p>
            {password.length > 0 && password.length < 4 && (
              <p className="password-min-hint">At least 4 characters recommended.</p>
            )}
            <button
              type="button"
              className="btn btn-primary send-transfer-btn"
              onClick={handleSend}
              disabled={sendStatus === 'sending'}
            >
              Send transfer
            </button>
          </div>
        </>
      )}
    </div>
  );
}
