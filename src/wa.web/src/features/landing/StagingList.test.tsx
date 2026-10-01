import { fireEvent, render, screen } from '@testing-library/react';
import App from '../../App';
import { useUploadEngine, __setUploadFn, __setDelayFn } from '../../core/upload/UploadEngine';
import { useToasts } from '../../core/ui/Toast';

// US-001-05 — Recover from a failed upload (FR-001-6 / AC-001-3).
// Integration tests for failed rows, retry button, overall status line, and toast.

const BLOCK_SIZE = 8 * 1024 * 1024; // 8 MiB

function makeFile(name: string, size = 1024): File {
  const file = new File([''], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

/** US-001-05: wait for a file to reach a specific status. */
async function waitForStatus(fileId: string, status: string, timeout = 2000): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const file = useUploadEngine.getState().files.find((f) => f.id === fileId);
    if (file?.status === status) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(`Timeout waiting for file ${fileId} to reach status ${status}`);
}

describe('US-001-05 — Recover from a failed upload (StagingList)', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useToasts.getState().clear();
  });

  it('failed row renders with danger styling + Retry button (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — failed row shows .file-row--failed class + Retry button.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    // Stage a file that will fail during upload.
    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('fail.bin', BLOCK_SIZE)] } });

    // Mock fetch to return a valid draft response.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        draftId: 'draft-123',
        files: [{ uploadUrl: 'http://example.com/upload/1' }],
      }),
    });

    // Mock upload function to always fail.
    __setUploadFn(async (_block, _url) => {
      throw new Error('Block upload failed');
    });
    __setDelayFn(async () => {}); // no delay in tests

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    fireEvent.click(sendButton);

    // Wait for the file to fail.
    const fileId = useUploadEngine.getState().files[0].id;
    await waitForStatus(fileId, 'failed');

    // Verify failed row styling.
    const list = screen.getByRole('list', { name: 'Staged files' });
    const failedRow = list.querySelector('.file-row--failed');
    expect(failedRow).not.toBeNull();

    // Verify "Upload failed — retry" text replaces size label.
    expect(failedRow?.textContent).toContain('Upload failed — retry');

    // Verify Retry button is present with correct aria-label.
    const retryButton = screen.getByRole('button', { name: 'Retry fail.bin' });
    expect(retryButton).toBeDefined();
    expect(retryButton.className).toContain('file-retry-button');
  });

  it('clicking Retry calls store method and resumes upload (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — clicking Retry resumes from last completed block.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('retry.bin', BLOCK_SIZE * 2)] } });

    // Mock fetch to return a valid draft response.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        draftId: 'draft-123',
        files: [{ uploadUrl: 'http://example.com/upload/1' }],
      }),
    });

    // First attempt: fail on second block.
    let shouldFail = true;
    __setUploadFn(async (_block, _url) => {
      if (shouldFail) throw new Error('Block upload failed');
    });
    __setDelayFn(async () => {}); // no delay in tests

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    fireEvent.click(sendButton);

    const fileId = useUploadEngine.getState().files[0].id;
    await waitForStatus(fileId, 'failed');

    // Now make upload succeed for retry.
    shouldFail = false;

    // Click Retry button.
    const retryButton = screen.getByRole('button', { name: 'Retry retry.bin' });
    fireEvent.click(retryButton);

    // Wait for file to complete.
    await waitForStatus(fileId, 'done');

    const state = useUploadEngine.getState();
    expect(state.files[0].status).toBe('done');
    expect(state.files[0].progress).toBe(1);
  });

  it('all-fail shows "paused" overall line (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — all files fail → overall state is "paused".
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    // Stage 3 files that will all fail.
    fireEvent.drop(zone, {
      dataTransfer: {
        files: [makeFile('a.bin', BLOCK_SIZE), makeFile('b.bin', BLOCK_SIZE), makeFile('c.bin', BLOCK_SIZE)],
      },
    });

    // Mock fetch to return a valid draft response.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        draftId: 'draft-123',
        files: [
          { uploadUrl: 'http://example.com/upload/1' },
          { uploadUrl: 'http://example.com/upload/2' },
          { uploadUrl: 'http://example.com/upload/3' },
        ],
      }),
    });

    // Mock upload function to always fail.
    __setUploadFn(async (_block, _url) => {
      throw new Error('Block upload failed');
    });
    __setDelayFn(async () => {}); // no delay in tests

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    fireEvent.click(sendButton);

    // Wait for all files to fail.
    const files = useUploadEngine.getState().files;
    await waitForStatus(files[0].id, 'failed');
    await waitForStatus(files[1].id, 'failed');
    await waitForStatus(files[2].id, 'failed');

    // Verify overall status line shows "paused".
    const statusLine = document.querySelector('.staging-overall');
    expect(statusLine?.textContent).toContain('Upload paused — 3 files need attention.');

    // Verify each row has a Retry button.
    const retryButtons = screen.getAllByRole('button', { name: /Retry / });
    expect(retryButtons).toHaveLength(3);
  });

  it('toast on first failure (info kind) (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — toast "Some files need attention" on first failure only.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('fail.bin', BLOCK_SIZE)] } });

    // Mock fetch to return a valid draft response.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        draftId: 'draft-123',
        files: [{ uploadUrl: 'http://example.com/upload/1' }],
      }),
    });

    // Mock upload function to always fail.
    __setUploadFn(async (_block, _url) => {
      throw new Error('Block upload failed');
    });
    __setDelayFn(async () => {}); // no delay in tests

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    fireEvent.click(sendButton);

    const fileId = useUploadEngine.getState().files[0].id;
    await waitForStatus(fileId, 'failed');

    // Verify toast was fired with info kind.
    const toasts = useToasts.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0].kind).toBe('info');
    expect(toasts[0].message).toBe('Some files need attention');
  });

  it('does not show overall status line when no files are failed', async () => {
    // US-001-05: no failed files → no "paused" line.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('ok.bin', BLOCK_SIZE)] } });

    // Mock fetch to return a valid draft response.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any -- jsdom fetch mock requires casting
    (window as any).fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        draftId: 'draft-123',
        files: [{ uploadUrl: 'http://example.com/upload/1' }],
      }),
    });

    // Mock upload function to always succeed.
    __setUploadFn(async (_block, _url) => {});
    __setDelayFn(async () => {}); // no delay in tests

    const sendButton = screen.getByRole('button', { name: /Send transfer/i });
    fireEvent.click(sendButton);

    const fileId = useUploadEngine.getState().files[0].id;
    await waitForStatus(fileId, 'done');

    // No "paused" line should be present.
    expect(screen.queryByText(/Upload paused/)).toBeNull();
  });
});
