import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import App from './App.tsx';

const GB = 1024 ** 3;

function makeFile(name: string, size: number, type = ''): File {
  const f = new File(['x'], name, type ? { type } : undefined);
  Object.defineProperty(f, 'size', { value: size });
  return f;
}

/** jsdom has no DataTransfer; attach a stand-in with just `files`, which React's onDrop reads. */
function simulateDrop(target: Element, files: File[]) {
  act(() => {
    const dragOver = new window.Event('dragover', { bubbles: true });
    (dragOver as unknown as { dataTransfer: unknown }).dataTransfer = { files };
    target.dispatchEvent(dragOver);

    const drop = new window.Event('drop', { bubbles: true });
    (drop as unknown as { dataTransfer: unknown }).dataTransfer = { files };
    target.dispatchEvent(drop);
  });
}

function pasteImage(name: string, size: number) {
  const data = { files: [makeFile(name, size, 'image/png')] };
  act(() => {
    window.dispatchEvent(
      Object.assign(new window.CustomEvent('paste'), { clipboardData: data }),
    );
  });
}

beforeEach(() => {
  // Pending forever: AC #1 only asserts fetch was *not* called (no upload started).
  vi.spyOn(window, 'fetch').mockImplementation(
    () => new Promise<Response>(() => undefined),
  );
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('App landing (US-001-01)', () => {
  it('renders the ProtoDrop landing', () => {
    render(<App />);
    expect(screen.getByText('ProtoDrop')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Upload files' })).toBeDefined();
  });

  it('stages dropped files with names, sizes and a combined total — no upload yet (AC #1)', () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    // Story AC #1: "drag 3 files (total 2 GB)" — 1 + 768 MB + 256 MB == 2.0 GB.
    const files: File[] = [
      makeFile('movie.mp4', GB),
      makeFile('photo.png', (GB * 3) / 4),
      makeFile('notes.txt', GB / 4),
    ];
    simulateDrop(zone, files);

    expect(screen.getByText('movie.mp4')).toBeDefined();
    expect(screen.getByText('1.0 GB')).toBeDefined();
    expect(screen.getByText('photo.png')).toBeDefined();
    expect(screen.getByText('768.0 MB')).toBeDefined();
    expect(screen.getByText('notes.txt')).toBeDefined();
    expect(screen.getByText('256.0 MB')).toBeDefined();
    // UI-Reference §5.1: "N files · X" — 1 GB + 768 MB + 256 MB == 2.0 GB.
    expect(screen.getByText('3 files · 2.0 GB')).toBeDefined();
    expect(window.fetch).not.toHaveBeenCalled();
  });

  it('stages a clipboard image on paste with a derived name (AC #2)', () => {
    render(<App />);

    pasteImage('', GB / 16);

    expect(screen.getByText('pasted-image.png')).toBeDefined();
    expect(screen.getByText('1 file · 64.0 MB')).toBeDefined();
  });

  it('stages a second pasted image under a non-colliding name', () => {
    render(<App />);

    pasteImage('', GB / 16);
    pasteImage('', GB / 16);

    expect(screen.getByText('pasted-image.png')).toBeDefined();
    expect(screen.getByText('pasted-image-2.png')).toBeDefined();
  });

  it('stages files from the native picker (AC #3, phone path)', () => {
    render(<App />);
    const input = document.querySelector<HTMLInputElement>('input[type="file"]');
    if (!input) throw new Error('picker input missing');

    const picked: File[] = [
      makeFile('a.png', 1024 ** 3 / 2),
      makeFile('b.mp4', 1024 ** 3 / 2),
    ];
    Object.defineProperty(input, 'files', { value: picked });
    fireEvent.change(input);

    expect(screen.getByText('a.png')).toBeDefined();
    expect(screen.getByText('b.mp4')).toBeDefined();
    expect(screen.getByText('2 files · 1.0 GB')).toBeDefined();
  });

  it('removes staged files via the row remove control', () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });
    simulateDrop(zone, [makeFile('one.mp4', GB), makeFile('two.png', GB / 2)]);

    fireEvent.click(screen.getByRole('button', { name: 'Remove one.mp4' }));

    expect(screen.queryByText('one.mp4')).toBeNull();
    expect(screen.getByText('two.png')).toBeDefined();
  });
});
