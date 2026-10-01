import { fireEvent, render, screen } from '@testing-library/react';
import App from '../../App';
import { useUploadEngine } from '../../core/upload/UploadEngine';
import { useToasts } from '../../core/ui/Toast';

// US-001-03 — Size validation (client pre-check + Send button).
// Acceptance criteria: violation message names exact cap, per-file row marking,
// disabled Send button until fixed, no bytes written while violated.

function makeFile(name: string, size = 1024): File {
  const file = new File([''], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('US-001-03 — Size validation (StagingList)', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useToasts.getState().clear();
  });

  it('displays a violation message and disables Send when total exceeds limit', async () => {
    // Story happy path: 6.2 GB selection vs 5 GB limit triggers exact-cap message + disabled button.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, {
      dataTransfer: {
        files: [makeFile('a.bin', 3_200_000_000), makeFile('b.bin', 3_168_709_120)], // 6.2 GB total
      },
    });

    expect(screen.getByText(/files.*GB/)).toBeDefined();
    const alert = screen.getByRole('alert');
    expect(alert.textContent).toContain(
      'exceeds the 5 GB limit'
    );

    const sendButton: HTMLButtonElement = screen.getByLabelText(/Send transfer/);
    // React sets aria-disabled="true" for disabled buttons via this prop pattern.
    expect(sendButton.getAttribute('aria-disabled')).toBe('true');
    expect(sendButton.style.opacity).toBe('0.55');
  });

  it('marks a row with danger styling and tooltip when a single file exceeds per-file cap', async () => {
    // EC-001-2 / story alt flow: one 7 GB file vs 5 GB per-file limit.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('big.bin', 7_000_000_000)] } });

    // Total line shows the size (no "exceeds" since total is under 5 GB)
    const list = screen.getByRole('list', { name: 'Staged files' });
    const sizeElement = list.querySelector('.staging-list li .file-size');
    // 7 GB / 1024^3 ≈ 6.51 → formatBytes shows "6.5 GB"
    expect(sizeElement?.textContent).toMatch(/6.*GB/);
    // Alert specifically mentions per-file limit
    expect(screen.getByRole('alert').textContent).toContain(
      'per-file limit is 5 GB'
    );

    // Row should have the danger class via .file-row--over-limit
    const stagedList = screen.getByRole('list', { name: 'Staged files' });
    const dangerRow = stagedList.querySelector('.file-row--over-limit');
    expect(dangerRow).not.toBeNull();
    // querySelector returns Node | null, cast to Element for .textContent access in tests
    const nameEl = dangerRow?.querySelector<Element>('li .file-name') as HTMLLIElement;
    expect(nameEl.textContent).toMatch(/big\.bin/i);

    // Tooltip / title attribute carries the limit info
    expect(dangerRow?.getAttribute('title')).toContain('5 GB');

    const sendButton: HTMLButtonElement = screen.getByLabelText(/Send transfer/);
    expect(sendButton.getAttribute('aria-disabled')).toBe('true');
  });

  it('re-enables Send and clears the message when the violating file is removed', async () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    // Stage two files that together exceed the limit
    fireEvent.drop(zone, {
      dataTransfer: {
        files: [makeFile('a.bin', 3_200_000_000), makeFile('b.bin', 3_168_709_120)],
      },
    });

    expect(screen.getByRole('alert')).not.toBeNull();

    // Remove one file — total drops below 5 GB, Send should re-enable
    fireEvent.click(screen.getByRole('button', { name: 'Remove a.bin' }));

    // Wait for the staging list to update (the violation div is removed from DOM when hasViolation=false)
    await screen.findByText(/GB/, { selector: '.staging-list li .file-size' });

    expect(screen.queryByRole('alert')).toBeNull();
    const sendButton = screen.getByLabelText(/Send transfer/);
    // When enabled, aria-disabled is set to "false" by this prop pattern
    expect(sendButton.getAttribute('aria-disabled')).toBe('false');
  });

  it('does not render the Send button when the list is empty', async () => {
    render(<App />);

    // No files staged → StagingList returns null, no Send button present
    expect(screen.queryByRole('button', { name: /Send transfer/i })).toBeNull();
  });

  it('accepts a zero-byte file without triggering a violation and shows "0 B" in the row and total line', async () => {
    // EC-001-3: 0-byte files are accepted but flagged in their row.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('empty.txt', 0)] } });

    expect(screen.getByText(/file.*0 B/)).toBeDefined();
    // Zero bytes does not exceed any limit — no violation message should appear
    expect(screen.queryByRole('alert')).toBeNull();

    const list = screen.getByRole('list', { name: 'Staged files' });
    expect(list.querySelector('.file-row--over-limit')).toBeNull();

    const sendButton: HTMLButtonElement = screen.getByLabelText(/Send transfer/);
    // When enabled, aria-disabled is set to "false" by this prop pattern
    expect(sendButton.getAttribute('aria-disabled')).toBe('false');
  });

  it('handles a mix of valid and violating files by showing only the relevant violation', async () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    // One file exceeds per-file cap; total is within limit
    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('huge.bin', 6_000_000_000)] } });

    expect(screen.getByRole('alert').textContent).toContain(
      'per-file limit'
    );
    // Total line shows the actual size (no "exceeds" wording because total is fine)
    const list = screen.getByRole('list', { name: 'Staged files' });
    const fileSizeElement = list.querySelector('.staging-list li .file-size');
    expect(fileSizeElement?.textContent).toMatch(/6.*GB/);
  });

  it('calls start() with the draft payload when Send is clicked without violations', async () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, {
      dataTransfer: {
        files: [makeFile('test.txt', 1024), makeFile('hello.pdf', 2048)],
      },
    });

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    expect(sendButton.getAttribute('aria-disabled')).toBe('false');

    // Mock fetch to return a valid draft response
    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        draftId: 'draft-123',
        expiresInSec: 7200,
        files: [
          { uploadUrl: 'http://example.com/upload/1', contentLength: 1024 },
          { uploadUrl: 'http://example.com/upload/2', contentLength: 2048 },
        ],
      }),
    });

    fireEvent.click(sendButton);

    // Wait for the async fetch + start() to complete — poll until status flips to 'uploading'.
    const waitForUpload = () =>
      new Promise<void>((resolve) => {
        if (useUploadEngine.getState().files.every((f) => f.status === 'uploading')) {
          resolve();
          return;
        }
        setTimeout(() => void waitForUpload(), 10);
      });
    await waitForUpload();

    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    expect((window as any).fetch).toHaveBeenCalledWith(
      '/api/v1/transfers/draft',
      expect.objectContaining({
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          files: [
            { name: 'test.txt', sizeBytes: 1024 },
            { name: 'hello.pdf', sizeBytes: 2048 },
          ],
        }),
      })
    );

    // start() should have been called with the draft payload — files are now uploading
    const state = useUploadEngine.getState();
    expect(state.files).toHaveLength(2);
    expect(state.files.every((file) => file.status === 'uploading')).toBe(true);
  });

  it('shows an error toast when the draft POST returns a TRANSFER_SIZE_EXCEEDED response', async () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('big.bin', 6_000_000_000)] } });

    // The Send button is visually dimmed (opacity 0.55) but still interactive
    const sendButton = screen.getByRole('button', { name: /Send transfer/i });

    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ code: 'TRANSFER_SIZE_EXCEEDED', details: { bytes: 6_000_000_000 } }),
    });

    await fireEvent.click(sendButton);

    // Wait for the error toast to appear
    await screen.findByText(/exceeds the 5 GB limit/);

    expect(useToasts.getState().toasts).toHaveLength(1);
    // The actual message format from StagingList: "Transfer size X GB exceeds the plan limit of Y GB."
    expect(useToasts.getState().toasts[0].message).toMatch(/exceeds the plan limit/);
  });

  it('shows a generic error toast on network failure', async () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('a.bin', 1024)] } });

    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockRejectedValue(new Error('Network error'));

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    await fireEvent.click(sendButton);

    expect(useToasts.getState().toasts[0].message).toBe(
      'Network error. Please try again.'
    );
  });
});
