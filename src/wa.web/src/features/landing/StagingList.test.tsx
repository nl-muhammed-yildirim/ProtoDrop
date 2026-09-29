import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import App from '../../App';
import { useUploadEngine } from '../../core/upload/UploadEngine';
import { useToasts } from '../../core/ui/Toast';

// US-001-02 — Stage and remove multiple files (closure slice of T-012 web part)
// Acceptance criteria: empty-state, "0 B" flag for 0-byte files, duplicate names independently removable.

function makeFile(name: string, size = 1024): File {
  const file = new File([''], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('US-001-02 — stage and remove files (StagingList)', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useToasts.getState().clear();
  });

  it('removing the last file returns the UI to the empty drop-zone state', () => {
    // Story edge case: removing the LAST file returns the UI to the empty drop-zone state.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, {
      dataTransfer: { files: [makeFile('a.txt'), makeFile('b.txt')] },
    });

    expect(screen.queryByRole('list', { name: 'Staged files' })).not.toBeNull();
    expect(screen.getByText('2 files · 2 KB')).toBeDefined();

    // Remove first file
    fireEvent.click(screen.getByRole('button', { name: 'Remove a.txt' }));
    expect(screen.queryByText('a.txt')).toBeNull();
    expect(screen.getByText('1 file · 1 KB')).toBeDefined();

    // Remove last (second) file — should return to empty drop zone state
    fireEvent.click(screen.getByRole('button', { name: 'Remove b.txt' }));
    expect(screen.queryByRole('list', { name: 'Staged files' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Upload files' })).toBeDefined(); // drop zone back
  });

  it('accepts a 0-byte file and flags its row as "0 B" (EC-001-3)', () => {
    // EC-001-3: a 0-byte file is accepted, but its row shows "0 B" so the sender is not surprised.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('empty.txt', 0)] } });

    expect(screen.getByText('empty.txt')).toBeDefined();       // row name present
    expect(screen.getByText('0 B')).toBeDefined();             // row size flag (0-byte file)
    expect(screen.getByText('1 file · 0 B')).toBeDefined();    // total line, deterministic
  });

  it('stages two files with the same name and allows independent removal of each', () => {
    // AC-2 staging half + EC-001-2: duplicate names are both staged and independently removable.
    render(<App />);
    const zone = screen.getByRole('button', { name: 'Upload files' });

    fireEvent.drop(zone, { dataTransfer: { files: [makeFile('report.pdf'), makeFile('report.pdf')] } });

    expect(screen.queryAllByText('report.pdf').length).toBe(2); // both staged

    fireEvent.click(screen.getAllByRole('button', { name: 'Remove report.pdf' })[0]);
    expect(screen.getByText('report.pdf')).toBeDefined();       // exactly one left
    expect(screen.getByText('1 file · 1 KB')).toBeDefined();   // default makeFile size is 1024
  });
});
