import { describe, expect, it } from 'vitest';
import {
  formatBytes,
  nextPastedImageName,
  stageFile,
  stagePastedFiles,
  stageSummaryText,
} from './fileStaging';

const GB = 1024 ** 3;

/** File with a logical size — no giant buffers needed in tests. */
function file(name: string, size: number, type = ''): File {
  const f = new File(['x'], name, type ? { type } : undefined);
  Object.defineProperty(f, 'size', { value: size });
  return f;
}

describe('stageFile (TA-8.3 shape)', () => {
  it('exposes public data and keeps the File for a later engine', () => {
    const f = file('movie.mp4', 2_000_000);
    const entry = stageFile(f);

    expect(entry.name).toBe('movie.mp4');
    expect(entry.size).toBe(2_000_000);
    expect(entry.file).toBe(f);
    expect(typeof entry.id).toBe('string');
    expect(entry.id.length).toBeGreaterThan(0);
  });

  it('gives every staged file a unique id', () => {
    const a = stageFile(file('a.bin', 10));
    const b = stageFile(file('b.bin', 20));
    expect(a.id).not.toBe(b.id);
  });
});

describe('stageSummaryText (UI §5.1 "N files · X")', () => {
  it('shows the combined size, not a per-file list', () => {
    // 1 + 0.5 + 0.125 GiB = exactly 1.625 GiB -> one decimal: 1.6 GB
    expect(
      stageSummaryText([file('a.mp4', GB), file('b.png', GB / 2), file('c.txt', GB / 8)]),
    ).toBe('3 files · 1.6 GB');
  });

  it('uses the singular form for a single staged file', () => {
    expect(stageSummaryText([file('solo.png', 512)])).toBe('1 file · 512 B');
  });
});

describe('pasted image naming', () => {
  it('derives pasted-image.png, then increments on collisions', () => {
    expect(nextPastedImageName([])).toBe('pasted-image.png');
    expect(nextPastedImageName(['pasted-image.png'])).toBe('pasted-image-2.png');
    expect(nextPastedImageName(['pasted-image.png', 'pasted-image-2.png'])).toBe(
      'pasted-image-3.png',
    );
  });

  it('names consecutive pastes uniquely and keeps non-images named as-is', () => {
    const image = file('', 120, 'image/png');
    const plain = file('notes.txt', 40, 'text/plain');

    const first = stagePastedFiles([image], []);
    expect(first[0].name).toBe('pasted-image.png');

    const second = stagePastedFiles([image, plain], first.map(e => e.name));
    expect(second[0].name).toBe('pasted-image-2.png');
    expect(second[1].name).toBe('notes.txt');
  });
});

describe('formatBytes (TA-8.5: base 1024, one decimal above bytes)', () => {
  it.each([
    [0, '0 B'],
    [512, '512 B'],
    [1_024, '1.0 KB'],
    [65_536, '64.0 KB'],
    [1_024 ** 2, '1.0 MB'],
    [2 * (1_024 ** 3), '2.0 GB'],
    [37 * (1_024 ** 3) / 8, '4.6 GB'],
  ] as const)('%i bytes -> %s', (input, expected) => {
    expect(formatBytes(input)).toBe(expected);
  });
});
