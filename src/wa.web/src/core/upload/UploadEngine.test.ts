import { beforeEach, describe, expect, it } from 'vitest';
import { useUploadEngine } from './UploadEngine';

// US-001-01 stages client-side only — nothing is uploaded until "Send." (US-001-03).
function makeFile(name: string, size = 1024): File {
  const file = new File([''], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('useUploadEngine (TA-8.3 state) — US-001-01 staging only', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
  });

  it('stages files in selection order, queued with no progress', () => {
    useUploadEngine.getState().addFiles([makeFile('a.txt'), makeFile('b.txt')]);

    const state = useUploadEngine.getState();
    expect(state.files.map((file) => file.name)).toEqual(['a.txt', 'b.txt']);
    expect(
      state.files.every((file) => file.status === 'queued' && file.progress === 0),
    ).toBe(true);
  });

  it('reports the combined size with no bytes written before "Send."', () => {
    useUploadEngine
      .getState()
      .addFiles([makeFile('a.bin', 1073741824), makeFile('b.bin', 536870912)]);

    const state = useUploadEngine.getState();
    expect(state.overall.totalBytes).toBe(1610612736);
    expect(state.overall.sentBytes).toBe(0); // "no bytes written" AC
  });

  it('keeps duplicate names — the user may stage two files with the same name (EC-001-2)', () => {
    useUploadEngine.getState().addFiles([makeFile('report.pdf'), makeFile('report.pdf')]);

    const state = useUploadEngine.getState();
    expect(state.files.map((file) => file.name)).toEqual(['report.pdf', 'report.pdf']);
    expect(new Set(state.files.map((file) => file.id)).size).toBe(2);
  });

  it('flattens folder drops to base names via webkitRelativePath', () => {
    const nested = makeFile('a.png');
    Object.defineProperty(nested, 'webkitRelativePath', { value: 'Photos/a.png' });
    useUploadEngine.getState().addFiles([nested]);

    expect(useUploadEngine.getState().files.map((file) => file.name)).toEqual(['a.png']);
  });

  it('remove() drops the row and recomputes the total', () => {
    const first = makeFile('a.txt');
    useUploadEngine.getState().addFiles([first, makeFile('b.txt')]);
    useUploadEngine.getState().remove(useUploadEngine.getState().files[0].id);

    const state = useUploadEngine.getState();
    expect(state.files.map((file) => file.name)).toEqual(['b.txt']);
    expect(state.overall.totalBytes).toBe(1024);
  });

  it('reset() clears staging and totals', () => {
    useUploadEngine.getState().addFiles([makeFile('a.txt')]);
    useUploadEngine.getState().reset();

    const state = useUploadEngine.getState();
    expect(state.files).toEqual([]);
    expect(state.overall).toEqual({ sentBytes: 0, totalBytes: 0 });
  });
});
