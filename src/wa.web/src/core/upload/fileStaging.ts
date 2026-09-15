/**
 * US-001-01 / US-001-02 (T-031, T-032) — staging model for the landing upload
 * surface (FR-001-1/2, EC-001-*).
 *
 * Mirrors the TA-8.3 `StageEntry` contract so the Zustand upload store can consume
 * it unchanged later: an entry carries the public data ({id, name, size}) and keeps
 * the browser `File` attached for UploadEngine to consume in T-034 — no bytes are
 * written while files sit here (happy path AC #1, "no upload has started yet").
 *
 * T-032 adds the stable **Send.** surface: `buildSendPayload` snapshots the list in
 * selection order without de-duping names; `removeStagedFiles`/`canSend` drive the
 * pre-send remove/availability rules incl. the post-"Send." lock (edge case 2).
 */

export interface StagedFile {
  /** Stable identity (`crypto.randomUUID()`, see TA-8.4) — also the React list key. */
  readonly id: string;
  /** Exact file name as presented by the OS / picker (rename is handled at copy time, EC-001-1). */
  readonly name: string;
  /** Size in bytes (1024-based display via {@link formatBytes}). */
  readonly size: number;
  /** Underlying `File` — consumed later by the upload engine, not sniffed here. */
  readonly file: File;
}

const BASE_NAME = 'pasted-image';

function makeId(): string {
  const c = globalThis.crypto;
  if (c && typeof c.randomUUID === 'function') {
    return c.randomUUID();
  }
  // Non-crypto fallback keeps unit tests deterministic-friendly without a browser.
  return `file-${Math.random().toString(36).slice(2, 10)}`;
}

/** Stage one file dropped, picked or pasted; HEIC / unknown MIME are accepted as-is (EC-001-4). */
export function stageFile(file: File): StagedFile {
  return { id: makeId(), name: file.name, size: file.size, file };
}

/**
 * Derive the next clipboard image file name (`pasted-image.png`, `pasted-image-2.png`, …)
 * that does not collide with names already in the staging list.
 */
export function nextPastedImageName(taken: Iterable<string>): string {
  const used = new Set(taken);
  if (!used.has(`${BASE_NAME}.png`)) {
    return `${BASE_NAME}.png`;
  }
  let index = 2;
  while (used.has(`${BASE_NAME}-${index}.png`)) {
    index += 1;
  }
  return `${BASE_NAME}-${index}.png`;
}

/**
 * Stage clipboard files. Image payloads get derived names; anything else keeps the OS name.
 */
export function stagePastedFiles(
  clipboardFiles: ArrayLike<File>,
  takenNames: Iterable<string>,
): StagedFile[] {
  const used = new Set(takenNames);
  const staged: StagedFile[] = [];
  for (let i = 0; i < clipboardFiles.length; i += 1) {
    const file = clipboardFiles[i];
    const isImage = file.type.startsWith('image/');
    const name = isImage ? nextPastedImageName(used) : file.name;
    used.add(name);
    staged.push({ id: makeId(), name, size: file.size, file });
  }
  return staged;
}

const BYTE_LADDER = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/**
 * Selection-order snapshot of everything staged (the stable "Send." payload, T-032
 * US-001-02 AC #2) — names repeat verbatim and entries keep list order; no de-duping.
 */
export function buildSendPayload(stagedFiles: readonly StagedFile[]): SendItem[] {
  return stagedFiles.map(entry => ({ name: entry.name, size: entry.size, file: entry.file }));
}

/**
 * One entry of the "Send." payload (stable, T-032 AC #2): what UploadEngine will
 * upload later in T-034. Names arrive verbatim from the staging list — duplicates
 * stay distinct because each `File` keeps its identity here.
 */
export interface SendItem {
  /** Exact staged file name, repeated verbatim for duplicate names. */
  readonly name: string;
  /** Size in bytes as staged. */
  readonly size: number;
  /** Underlying `File` consumed by the upload engine in T-034. */
  readonly file: File;
}

/**
 * Remove one staged entry by id (✕ button). Pure and allocation-cheap; removing the
 * last entry naturally returns the caller to the empty drop-zone state.
 */
export function removeStagedFiles(
  staged: readonly StagedFile[],
  id: string,
): StagedFile[] {
  return staged.filter(entry => entry.id !== id);
}

/** "Send." availability (UI note on US-001-02): usable iff the list is non-empty and nothing has been sent yet. */
export function canSend(staged: readonly StagedFile[], alreadySent: boolean): boolean {
  return staged.length > 0 && !alreadySent;
}

/** Human-friendly bytes, base 1024 with one decimal for units above bytes
 * (TA-8.5; total line copy is defined by UI §5.1, e.g. "3 files · 2.1 GB").
 */
export function formatBytes(sizeInBytes: number): string {
  let value = sizeInBytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < BYTE_LADDER.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }
  if (unitIndex === 0) {
    return `${Math.trunc(value)} B`;
  }
  return `${value.toFixed(1)} ${BYTE_LADDER[unitIndex]}`;
}

/**
 * Total staged line, UI-Reference §5.1: "N files · X GB" — count label + combined size.
 */
export function stageSummaryText(staged: ReadonlyArray<Readonly<{ size: number }>>): string {
  const count = staged.length;
  const totalBytes = staged.reduce((sum, entry) => sum + entry.size, 0);
  const unitWord = count === 1 ? 'file' : 'files';
  return `${count} ${unitWord} · ${formatBytes(totalBytes)}`;
}
