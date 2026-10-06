import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import App from '../../App';
import { useRecipientStore } from './RecipientStore';

// US-003-01 — Open a transfer link with no account (AC-003-1, FR-003-1, FR-003-2).
// Integration tests for the recipient page: file list, sizes, download buttons, note, error state.

const originalFetch = window.fetch;

/** Wait for the recipient store to reach a specific status. */
async function waitForRecipientStatus(status: string, timeout = 2000): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    if (useRecipientStore.getState().status === status) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(`Timeout waiting for recipient status to reach ${status}`);
}

describe('US-003-01 — Open a transfer link with no account', () => {
  beforeEach(() => {
    useRecipientStore.getState().reset();
    // Set pathname before render so App.tsx detects the recipient route.
    window.history.pushState('', '', '/t/ABCDEF12');
  });

  afterEach(() => {
    (window as any).fetch = originalFetch;
    // Reset pathname back to root for other test files.
    window.history.pushState('', '', '/');
  });

  it('renders file list with sender name and human-readable sizes', async () => {
    (window as any).fetch = vi.fn().mockImplementation(async (_url: string) => {
      return new Response(
        JSON.stringify({
          from: 'Studio Nova',
          note: null,
          files: [
            { fileId: 'f1', name: 'render.mp4', sizeBytes: 2147483648 },
            { fileId: 'f2', name: 'poster.png', sizeBytes: 5242880 },
          ],
          hasDownloadAll: true,
          downloadsLeft: 99,
          passwordRequired: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    });

    render(<App />);
    await waitForRecipientStatus('active');

    // Assert sender name is visible.
    expect(screen.getByText('From: Studio Nova')).toBeDefined();

    // Assert both file names are visible.
    expect(screen.getByText('render.mp4')).toBeDefined();
    expect(screen.getByText('poster.png')).toBeDefined();

    // Assert human-readable sizes (formatBytes: 2147483648 → "2 GB", 5242880 → "5 MB").
    expect(screen.getByText('2 GB')).toBeDefined();
    expect(screen.getByText('5 MB')).toBeDefined();
  });

  it('shows Download all button when multiple files', async () => {
    (window as any).fetch = vi.fn().mockImplementation(async (_url: string) => {
      return new Response(
        JSON.stringify({
          from: 'Studio Nova',
          note: null,
          files: [
            { fileId: 'f1', name: 'render.mp4', sizeBytes: 2147483648 },
            { fileId: 'f2', name: 'poster.png', sizeBytes: 5242880 },
          ],
          hasDownloadAll: true,
          downloadsLeft: 99,
          passwordRequired: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    });

    render(<App />);
    await waitForRecipientStatus('active');

    // Assert "Download all" button is present.
    expect(screen.getByRole('button', { name: /Download all/i })).toBeDefined();
  });

  it('hides Download all for single-file transfer', async () => {
    (window as any).fetch = vi.fn().mockImplementation(async (_url: string) => {
      return new Response(
        JSON.stringify({
          from: 'Studio Nova',
          note: null,
          files: [{ fileId: 'f1', name: 'render.mp4', sizeBytes: 2147483648 }],
          hasDownloadAll: false,
          downloadsLeft: 99,
          passwordRequired: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    });

    render(<App />);
    await waitForRecipientStatus('active');

    // Assert no "Download all" button.
    expect(screen.queryByRole('button', { name: /Download all/i })).toBeNull();
  });

  it('renders note when present', async () => {
    (window as any).fetch = vi.fn().mockImplementation(async (_url: string) => {
      return new Response(
        JSON.stringify({
          from: 'Studio Nova',
          note: 'Final renders — please review',
          files: [
            { fileId: 'f1', name: 'render.mp4', sizeBytes: 2147483648 },
          ],
          hasDownloadAll: false,
          downloadsLeft: 99,
          passwordRequired: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    });

    render(<App />);
    await waitForRecipientStatus('active');

    // Assert note is visible in a blockquote.
    const blockquote = document.querySelector('.recipient-note') as HTMLElement;
    expect(blockquote).not.toBeNull();
    expect(blockquote.textContent).toBe('Final renders — please review');
  });

  it('no sign-up wall on active page', async () => {
    (window as any).fetch = vi.fn().mockImplementation(async (_url: string) => {
      return new Response(
        JSON.stringify({
          from: 'Studio Nova',
          note: null,
          files: [
            { fileId: 'f1', name: 'render.mp4', sizeBytes: 2147483648 },
          ],
          hasDownloadAll: false,
          downloadsLeft: 99,
          passwordRequired: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    });

    render(<App />);
    await waitForRecipientStatus('active');

    // Assert no sign-up or create account text anywhere.
    expect(screen.queryByText(/sign up/i)).toBeNull();
    expect(screen.queryByText(/create account/i)).toBeNull();
  });

  it('shows error state for unknown linkId', async () => {
    (window as any).fetch = vi.fn().mockImplementation(async (_url: string) => {
      return new Response(
        JSON.stringify({ title: 'Not found' }),
        { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
      );
    });

    render(<App />);
    await waitForRecipientStatus('error');

    // Assert error state is visible.
    expect(screen.getByText('Something went wrong')).toBeDefined();
  });

  it('shows loading state initially', async () => {
    // Use a pending promise that never resolves to keep status in 'loading'.
    (window as any).fetch = vi.fn().mockImplementation(() => new Promise(() => {}));

    render(<App />);

    // Assert "Loading…" is visible before resolution.
    expect(screen.getByText('Loading…')).toBeDefined();
  });
});
