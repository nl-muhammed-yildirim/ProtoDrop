import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import App from '../../App';
import { useUploadEngine } from '../../core/upload/UploadEngine';
import { useToasts } from '../../core/ui/Toast';

function makeFile(name: string, size = 1024): File {
  const file = new File([''], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('US-001-01 — select files (landing)', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useToasts.getState().clear();
  });

  it('lists dropped files with the combined size and no bytes written yet', () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });
    fireEvent.drop(zone, {
      dataTransfer: {
        files: [makeFile('a.mp4', 1073741824), makeFile('b.mov', 536870912), makeFile('c.avi', 536870912)],
      },
    });

    expect(screen.getByText('a.mp4')).toBeDefined();
    expect(screen.getByText('b.mov')).toBeDefined();
    expect(screen.getByText('c.avi')).toBeDefined();
    // UI-Reference §4.2 total line: "N files · X GB"
    expect(screen.getByText('3 files · 2 GB')).toBeDefined();
    expect(useUploadEngine.getState().overall.sentBytes).toBe(0); // no bytes written AC
  });

  it('stages pasted clipboard images as pasted-image.png', () => {
    render(<App />);
    act(() => {
      const event = new Event('paste', { bubbles: true });
      Object.defineProperty(event, 'clipboardData', { value: { files: [makeFile('image.png')] } });
      document.dispatchEvent(event);
    });

    expect(screen.getByText('pasted-image.png')).toBeDefined();
  });

  it('numbers repeated pastes so names never collide', () => {
    useUploadEngine.getState().addFiles([makeFile('pasted-image.png')]);
    render(<App />);
    act(() => {
      const event = new Event('paste', { bubbles: true });
      Object.defineProperty(event, 'clipboardData', { value: { files: [makeFile('image.png')] } });
      document.dispatchEvent(event);
    });

    expect(screen.getByText('pasted-image-2.png')).toBeDefined();
  });

  it('stages files chosen with the file picker and resets the input for re-selection', () => {
    render(<App />);
    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!; // rendered by DropZone
    fireEvent.change(input, { target: { files: [makeFile('phone-1.jpg'), makeFile('phone-2.jpg')] } });

    expect(screen.getByText('phone-1.jpg')).toBeDefined();
    expect(screen.getByText('phone-2.jpg')).toBeDefined();
    expect(document.querySelector<HTMLInputElement>('input[type="file"]')!.value).toBe('');
  });

  it('shows "Some files were skipped" when a drop carries no files (EC-001-4)', () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });
    fireEvent.drop(zone, { dataTransfer: {} });

    expect(screen.getByText('Some files were skipped')).toBeDefined();
  });

  it('removes a staged file and updates the total line', () => {
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });
    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('a.txt'), makeFile('b.txt')] } });

    expect(screen.getByText('2 files · 2 KB')).toBeDefined();
    fireEvent.click(screen.getByRole('button', { name: 'Remove a.txt' }));

    expect(screen.queryByText('a.txt')).toBeNull();
    expect(screen.getByText('1 file · 1 KB')).toBeDefined();
  });

  it('flattens dropped folders to base names', () => {
    render(<App />);
    const nested = makeFile('a.png');
    Object.defineProperty(nested, 'webkitRelativePath', { value: 'Photos/a.png' });
    fireEvent.drop(screen.getByRole('button', { name: 'Upload files' }), {
      dataTransfer: { files: [nested] },
    });

    expect(screen.getByText('a.png')).toBeDefined();
  });
});
