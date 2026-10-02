import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import App from '../../App';
import { useUploadEngine, __setUploadFn, __setDelayFn } from '../../core/upload/UploadEngine';
import { useTransferStore } from '../../core/upload/TransferStore';
import { useToasts } from '../../core/ui/Toast';

// US-002-01 — Get a unique link for my files (AC-002-1 / EC-002-1).
// Integration tests for FinalizeButton visibility, finalize flow, LinkScreen rendering, and copy button.

const BLOCK_SIZE = 8 * 1024 * 1024; // 8 MiB

function makeFile(name: string, size = 1024): File {
  const file = new File([''], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

/** US-002-01: wait for a file to reach a specific status. */
async function waitForStatus(fileId: string, status: string, timeout = 2000): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const file = useUploadEngine.getState().files.find((f) => f.id === fileId);
    if (file?.status === status) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(`Timeout waiting for file ${fileId} to reach status ${status}`);
}

/** US-002-01: wait for transfer store to reach a specific status. */
async function waitForTransferStatus(status: string, timeout = 2000): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    if (useTransferStore.getState().status === status) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(`Timeout waiting for transfer status to reach ${status}`);
}

/** US-002-01: set up fetch mock that differentiates draft vs finalize endpoints. */
function setupFetchMock(fileCount = 1): void {
  const uploadUrls = Array.from({ length: fileCount }, (_, i) => `http://example.com/upload/${i + 1}`);

  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
  (window as any).fetch = vi.fn().mockImplementation(async (url: string) => {
    if (url.includes('/transfers/draft')) {
      return {
        ok: true,
        json: async () => ({
          draftId: 'draft-abc',
          files: uploadUrls.map((u) => ({ uploadUrl: u })),
        }),
      };
    }
    if (url.includes('/transfers/finalize')) {
      return {
        ok: true,
        json: async () => ({ linkId: '3f9k2a7x' }),
      };
    }
    if (url.includes('/transfers/send')) {
      return { ok: true, json: async () => ({}) };
    }
    return { ok: false, status: 404 };
  });
}

/** US-002-01: stage a file, send it, and wait for upload to complete. */
async function stageAndUpload(fileCount = 1): Promise<void> {
  const zone = screen.getByRole('button', { name: 'Upload files' });
  const files = Array.from({ length: fileCount }, (_, i) => makeFile(`file${i}.bin`, BLOCK_SIZE));
  fireEvent.drop(zone, { dataTransfer: { files } });

  setupFetchMock(fileCount);
  __setUploadFn(async (_block: Blob, _url: string) => {});
  __setDelayFn(async () => {});

  const sendButton = screen.getByRole('button', { name: /Send transfer/i });
  fireEvent.click(sendButton);

  const state = useUploadEngine.getState();
  for (const file of state.files) {
    await waitForStatus(file.id, 'done');
  }
}

// Fix 15: save original fetch for cleanup between tests.
const originalFetch = window.fetch;

describe('US-002-01 — Get a unique link for my files', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useTransferStore.getState().reset();
    useToasts.getState().clear();
  });

  afterEach(() => {
    (window as any).fetch = originalFetch; // Fix 15: restore original fetch.
  });

  it('link screen shows after all files upload + finalize succeeds (AC-002-1)', async () => {
    render(<App />);
    await stageAndUpload(1);

    // "Get my link" button should be visible now.
    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    expect(finalizeButton).toBeDefined();

    // Click to trigger finalize.
    fireEvent.click(finalizeButton);

    // Wait for transfer status to become 'ready'.
    await waitForTransferStatus('ready');

    // Assert: h2 "Your link is ready" is visible.
    const heading = screen.getByRole('heading', { level: 2, name: 'Your link is ready' });
    expect(heading).toBeDefined();

    // Assert: input with aria-label="Transfer link" has value containing "/t/3f9k2a7x".
    const input = screen.getByLabelText('Transfer link') as HTMLInputElement;
    expect(input.value).toContain('/t/3f9k2a7x');

    // Assert: copy button exists with text "Copy".
    const copyButton = screen.getByRole('button', { name: 'Copy link' });
    expect(copyButton.textContent).toBe('Copy');
  });

  it("copy button shows 'Copied ✓' after click (AC-002-1)", async () => {
    render(<App />);
    await stageAndUpload(1);

    // Trigger finalize.
    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Mock navigator.clipboard (jsdom doesn't have it by default).
    Object.defineProperty(window.navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      writable: true,
      configurable: true,
    });

    // Click the copy button. handleCopy is async (awaits clipboard.writeText),
    // so we need to wait for the state update to propagate.
    const copyButton = screen.getByRole('button', { name: 'Copy link' });
    fireEvent.click(copyButton);

    // Wait for the button text to update (async clipboard call resolves).
    const pollStart = Date.now();
    while (Date.now() - pollStart < 2000) {
      if (copyButton.textContent === 'Copied ✓') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    // Assert: button text is "Copied ✓" and has success class.
    expect(copyButton.textContent).toBe('Copied ✓');
    expect(copyButton.className).toContain('link-copy-btn--success');
  });

  it('finalize button only visible when all files done (AC-002-1)', async () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    // Stage a file but DON'T start upload (no Send click).
    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('a.bin', BLOCK_SIZE)] } });

    // Assert: no "Get my link" button in document.
    expect(screen.queryByRole('button', { name: /Get my link/i })).toBeNull();

    // Now stage another file and send both.
    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('b.bin', BLOCK_SIZE)] } });

    setupFetchMock(2);
    __setUploadFn(async (_block: Blob, _url: string) => {});
    __setDelayFn(async () => {});

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    fireEvent.click(sendButton);

    // Wait for both files to be done.
    const state = useUploadEngine.getState();
    for (const file of state.files) {
      await waitForStatus(file.id, 'done');
    }

    // Assert: "Get my link" button appears.
    expect(screen.getByRole('button', { name: /Get my link/i })).toBeDefined();
  });

  it('link input is read-only and shows full URL (AC-002-1)', async () => {
    render(<App />);
    await stageAndUpload(1);

    // Trigger finalize.
    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Assert: input has readOnly attribute true.
    const input = screen.getByLabelText('Transfer link') as HTMLInputElement;
    expect(input.readOnly).toBe(true);

    // Assert: value starts with window.location.origin + "/t/".
    expect(input.value).toBe(`${window.location.origin}/t/3f9k2a7x`);
  });
});

describe('US-002-02 — Send transfer by email', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useTransferStore.getState().reset();
    useToasts.getState().clear();
  });

  afterEach(() => {
    (window as any).fetch = originalFetch; // Fix 15: restore original fetch.
  });

  it('textarea renders with helper text after finalize succeeds', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    expect(textarea).toBeDefined();
    expect(screen.getByText('One per line — you can also separate with commas.')).toBeDefined();
  });

  it('valid addresses are accepted and sent (trimmed + lowercased)', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com, b@y.com ' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    await waitForTransferStatus('ready'); // status stays ready; wait for send to settle via store.
    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    expect(useTransferStore.getState().sendStatus).toBe('sent');
    const fetchMock = (window as any).fetch as ReturnType<typeof vi.fn>;
    const sendCall = fetchMock.mock.calls.find((call: unknown[]) => String(call[0]).includes('/transfers/send'));
    expect(sendCall).toBeDefined();
    const body = JSON.parse((sendCall as [string, { body?: string }])[1].body ?? '{}');
    expect(body.linkId).toBe('3f9k2a7x');
    expect(body.recipients).toEqual(['a@x.com', 'b@y.com']);
  });

  it('invalid address is rejected inline on blur; valid one still sent on submit', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'bad-address\nok@x.com' } });
    fireEvent.blur(textarea);

    expect(screen.getByRole('alert').textContent).toBe('Invalid: bad-address');

    // Per spec: submit sends the valid addresses (invalid ones are filtered out).
    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
    expect(useTransferStore.getState().sendStatus).toBe('sent');
    const fetchMock = (window as any).fetch as ReturnType<typeof vi.fn>;
    const sendCall = fetchMock.mock.calls.find((call: unknown[]) => String(call[0]).includes('/transfers/send'));
    const body = JSON.parse((sendCall as [string, { body?: string }])[1].body ?? '{}');
    expect(body.recipients).toEqual(['ok@x.com']);
  });

  it('duplicate addresses are deduplicated before send', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com\na@x.com' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
    expect(useTransferStore.getState().sendStatus).toBe('sent');
    const fetchMock = (window as any).fetch as ReturnType<typeof vi.fn>;
    const sendCall = fetchMock.mock.calls.find((call: unknown[]) => String(call[0]).includes('/transfers/send'));
    const body = JSON.parse((sendCall as [string, { body?: string }])[1].body ?? '{}');
    expect(body.recipients).toEqual(['a@x.com']);
  });

  it('over-limit input shows inline error "Maximum 20 recipients"', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    const addresses = Array.from({ length: 25 }, (_, i) => `user${i}@x.com`).join('\n');
    fireEvent.change(textarea, { target: { value: addresses } });
    fireEvent.blur(textarea);

    expect(screen.getByRole('alert').textContent).toBe('Maximum 20 recipients.');
  });

  it('confirmation state shows link + copy + "Send again"; clicking resets to empty textarea', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    expect(screen.getByRole('heading', { level: 2, name: 'Sent to 1 recipient' })).toBeDefined();
    const input = screen.getByLabelText('Transfer link') as HTMLInputElement;
    expect(input.value).toBe(`${window.location.origin}/t/3f9k2a7x`);
    expect(screen.getByRole('button', { name: 'Copy link' })).toBeDefined();

    const sendAgainButton = screen.getByRole('button', { name: 'Send again' });
    fireEvent.click(sendAgainButton);

    const newTextarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    expect(newTextarea.value).toBe('');
    expect(screen.getByRole('heading', { level: 2, name: 'Your link is ready' })).toBeDefined();
  });

  it('shows error toast when send API fails (400)', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Override fetch so /transfers/send returns 400.
    (window as any).fetch = vi.fn().mockImplementation(async (url: string) => {
      if (url.includes('/transfers/send')) {
        return { ok: false, status: 400, json: async () => ({ title: 'Bad request' }) };
      }
      return { ok: true, json: async () => ({}) };
    });

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    // Wait for the store to settle (sendStatus back to idle with error).
    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'idle' && useTransferStore.getState().sendError) break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    // Assert the inline error is shown.
    const alerts = screen.getAllByRole('alert');
    expect(alerts.some((el) => el.textContent === 'Bad request')).toBe(true);

    // Assert the toast was pushed via showToast (useToasts store).
    const toasts = useToasts.getState().toasts;
    expect(toasts.length).toBeGreaterThan(0);
  });
});

describe('US-002-03 — Password field on link screen', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useTransferStore.getState().reset();
    useToasts.getState().clear();
  });

  afterEach(() => {
    (window as any).fetch = originalFetch; // Fix 15: restore original fetch.
  });

  it('password field renders with helper text after finalize succeeds', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Assert the password input exists.
    const passwordInput = screen.getByLabelText('Password') as HTMLInputElement;
    expect(passwordInput).toBeDefined();
    expect(passwordInput.type).toBe('password');

    // Assert helper text is visible.
    expect(
      screen.getByText('Optional — only people with this password can download.'),
    ).toBeDefined();
  });

  it('show/hide toggle switches input type', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const passwordInput = screen.getByLabelText('Password') as HTMLInputElement;
    expect(passwordInput.type).toBe('password');

    // Click the toggle button (initially "Show password").
    const toggleBtn = screen.getByRole('button', { name: 'Show password' });
    fireEvent.click(toggleBtn);

    // Assert input type is now "text" and aria-label changed to "Hide password".
    expect(passwordInput.type).toBe('text');
    expect(screen.getByRole('button', { name: 'Hide password' })).toBeDefined();
  });

  it('password value is sent in send API body when set', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Type a password into the field.
    const passwordInput = screen.getByLabelText('Password') as HTMLInputElement;
    fireEvent.change(passwordInput, { target: { value: 'secret123' } });

    // Enter a recipient and send.
    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    // Wait for send to complete.
    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    expect(useTransferStore.getState().sendStatus).toBe('sent');

    // Inspect the fetch mock call to /transfers/send.
    const fetchMock = (window as any).fetch as ReturnType<typeof vi.fn>;
    const sendCall = fetchMock.mock.calls.find((call: unknown[]) => String(call[0]).includes('/transfers/send'));
    expect(sendCall).toBeDefined();
    const body = JSON.parse((sendCall as [string, { body?: string }])[1].body ?? '{}');
    expect(body.password).toBe('secret123');
  });

  it('no password key in send body when field is empty', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Leave password empty, enter a recipient and send.
    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    // Wait for send to complete.
    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    expect(useTransferStore.getState().sendStatus).toBe('sent');

    // Inspect the fetch mock call to /transfers/send.
    const fetchMock = (window as any).fetch as ReturnType<typeof vi.fn>;
    const sendCall = fetchMock.mock.calls.find((call: unknown[]) => String(call[0]).includes('/transfers/send'));
    expect(sendCall).toBeDefined();
    const body = JSON.parse((sendCall as [string, { body?: string }])[1].body ?? '{}');
    expect(body.password).toBeUndefined();
  });

  it('min-length hint appears for short non-empty passwords', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const passwordInput = screen.getByLabelText('Password') as HTMLInputElement;

    // Type a short password (2 chars).
    fireEvent.change(passwordInput, { target: { value: 'ab' } });

    // Assert the hint is visible.
    expect(
      screen.getByText('At least 4 characters recommended.'),
    ).toBeDefined();

    // Clear it and assert hint disappears.
    fireEvent.change(passwordInput, { target: { value: '' } });
    expect(screen.queryByText('At least 4 characters recommended.')).toBeNull();
  });

  it('send again resets the password field', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    // Set a password and send.
    const passwordInput = screen.getByLabelText('Password') as HTMLInputElement;
    fireEvent.change(passwordInput, { target: { value: 'secret123' } });

    const textarea = screen.getByPlaceholderText('one@x.com, two@y.com') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'a@x.com' } });

    const sendButton = screen.getByRole('button', { name: 'Send transfer' });
    fireEvent.click(sendButton);

    // Wait for sent.
    const start = Date.now();
    while (Date.now() - start < 2000) {
      if (useTransferStore.getState().sendStatus === 'sent') break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    // Click "Send again".
    const sendAgainButton = screen.getByRole('button', { name: 'Send again' });
    fireEvent.click(sendAgainButton);

    // Assert password field is back and empty.
    const newPasswordInput = screen.getByLabelText('Password') as HTMLInputElement;
    expect(newPasswordInput.value).toBe('');
  });

  it('min-length hint does not appear at exactly 4 characters', async () => {
    render(<App />);
    await stageAndUpload(1);

    const finalizeButton = screen.getByRole('button', { name: /Get my link/i });
    fireEvent.click(finalizeButton);
    await waitForTransferStatus('ready');

    const passwordInput = screen.getByLabelText('Password') as HTMLInputElement;
    fireEvent.change(passwordInput, { target: { value: 'abcd' } });

    // Exactly 4 chars: no hint should be visible.
    expect(screen.queryByText('At least 4 characters recommended.')).toBeNull();
  });
});
