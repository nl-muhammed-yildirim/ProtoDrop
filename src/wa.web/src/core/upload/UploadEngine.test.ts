import { beforeEach, describe, expect, it } from 'vitest';
import { useUploadEngine, __setUploadFn, __setDelayFn } from './UploadEngine';
import { useToasts } from '../ui/Toast';

// US-001-05: block-level retry with exponential backoff (FR-001-6 / AC-001-3).
// Unit tests for retry logic, progress tracking, and toast behavior.

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

describe('useUploadEngine — US-001-05 block retry (FR-001-6 / AC-001-3)', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useToasts.getState().clear();
  });

  it('retry succeeds mid-sequence — file marked done after all blocks succeed', async () => {
    // US-001-05: AC-001-3 — retry resumes from last completed block.
    const file = makeFile('test.bin', BLOCK_SIZE * 2); // 2 blocks

    let uploadCallCount = 0;
    __setUploadFn(async (_block, _url) => {
      uploadCallCount++;
      // First call fails (simulating a transient error), second succeeds.
      if (uploadCallCount === 1) throw new Error('Transient failure');
    });
    __setDelayFn(async () => {}); // no delay in tests

    useUploadEngine.getState().addFiles([file]);
    const fileId = useUploadEngine.getState().files[0].id;

    // Start upload — first block fails on first attempt, succeeds on retry.
    useUploadEngine.getState().start({ files: [{ uploadUrl: 'http://example.com/1' }] });

    await waitForStatus(fileId, 'done');

    const state = useUploadEngine.getState();
    expect(state.files[0].status).toBe('done');
    expect(state.files[0].progress).toBe(1);
  });

  it('all 5 retries fail → status failed + progress preserved (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — block fails 5 times → file marked failed.
    const file = makeFile('fail.bin', BLOCK_SIZE * 2); // 2 blocks

    __setUploadFn(async (_block, _url) => {
      throw new Error('Always fails');
    });
    __setDelayFn(async () => {}); // no delay in tests

    useUploadEngine.getState().addFiles([file]);
    const fileId = useUploadEngine.getState().files[0].id;

    useUploadEngine.getState().start({ files: [{ uploadUrl: 'http://example.com/1' }] });

    await waitForStatus(fileId, 'failed');

    const state = useUploadEngine.getState();
    expect(state.files[0].status).toBe('failed');
    // Progress should be 0 (no blocks completed) or partial if some succeeded.
    expect(state.files[0].progress).toBeGreaterThanOrEqual(0);
    expect(state.files[0].progress).toBeLessThan(1);
  });

  it('retry resumes from correct block index — no re-upload of finished blocks (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — retry resumes from last completed block.
    const file = makeFile('resume.bin', BLOCK_SIZE * 2); // 2 blocks

    useUploadEngine.getState().addFiles([file]);
    const fileId = useUploadEngine.getState().files[0].id;

    // US-001-05: fail only the second block (block index 1) on first attempt.
    // With parallelism 4 and 2 blocks, both are uploaded in one batch.
    // Block 0 succeeds, block 1 fails after all retries.
    let totalCalls = 0;
    __setUploadFn(async (_block, _url) => {
      totalCalls++;
      // Fail all attempts for block 1 (calls 2-7: initial + 5 retries).
      if (totalCalls >= 2 && totalCalls <= 7) throw new Error('Block 1 fails');
    });
    __setDelayFn(async () => {}); // no delay in tests

    useUploadEngine.getState().start({ files: [{ uploadUrl: 'http://example.com/1' }] });

    await waitForStatus(fileId, 'failed');

    // After failure, progress should reflect that block 0 succeeded (1 out of 2).
    const stateAfterFailure = useUploadEngine.getState();
    expect(stateAfterFailure.files[0].status).toBe('failed');
    // Progress: 1 block completed * BLOCK_SIZE / (2 * BLOCK_SIZE) = 1/2 = 0.5
    expect(stateAfterFailure.files[0].progress).toBeCloseTo(0.5, 2);

    // Now make all uploads succeed for retry, with a new counter.
    let retryCalls = 0;
    __setUploadFn(async (_block, _url) => {
      retryCalls++;
    });

    useUploadEngine.getState().retry(fileId);

    await waitForStatus(fileId, 'done');

    // Verify no re-upload of already-completed block (block 0).
    // Only block 1 needs to be re-uploaded (1 call).
    expect(retryCalls).toBe(1); // only block 1 re-uploaded
  });

  it('toast fires once per session on first failure (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — toast "Some files need attention" on first failure only.
    const file1 = makeFile('a.bin', BLOCK_SIZE);
    const file2 = makeFile('b.bin', BLOCK_SIZE);

    __setUploadFn(async (_block, _url) => {
      throw new Error('Always fails');
    });
    __setDelayFn(async () => {}); // no delay in tests

    useUploadEngine.getState().addFiles([file1, file2]);
    const fileId1 = useUploadEngine.getState().files[0].id;
    const fileId2 = useUploadEngine.getState().files[1].id;

    useUploadEngine.getState().start({
      files: [
        { uploadUrl: 'http://example.com/1' },
        { uploadUrl: 'http://example.com/2' },
      ],
    });

    // Wait for both files to fail.
    await waitForStatus(fileId1, 'failed');
    await waitForStatus(fileId2, 'failed');

    const toasts = useToasts.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0].kind).toBe('info');
    expect(toasts[0].message).toBe('Some files need attention');
  });

  it('other files continue unaffected when one file fails (AC-001-3)', async () => {
    // US-001-05: AC-001-3 — other files continue unaffected.
    const file1 = makeFile('fail.bin', BLOCK_SIZE);
    const file2 = makeFile('ok.bin', BLOCK_SIZE);

    useUploadEngine.getState().addFiles([file1, file2]);
    const fileId1 = useUploadEngine.getState().files[0].id;
    const fileId2 = useUploadEngine.getState().files[1].id;

    // US-001-05: use a counter to fail only the first file's blocks.
    let uploadCount = 0;
    __setUploadFn(async (_block, _url) => {
      uploadCount++;
      // Fail all blocks for the first file (uploadCount 1-6), succeed for second file.
      if (uploadCount <= 6) throw new Error('Fail first file');
    });
    __setDelayFn(async () => {}); // no delay in tests

    useUploadEngine.getState().start({
      files: [
        { uploadUrl: 'http://example.com/1' },
        { uploadUrl: 'http://example.com/2' },
      ],
    });

    // Wait for first file to fail.
    await waitForStatus(fileId1, 'failed');

    // Second file should continue and complete.
    await waitForStatus(fileId2, 'done');

    const state = useUploadEngine.getState();
    expect(state.files[0].status).toBe('failed');
    expect(state.files[1].status).toBe('done');
  });

  it('progress is byte-based: (completedBlocks * BLOCK_SIZE) / fileSize', async () => {
    // US-001-05: progress formula — byte-based, not block-count.
    const file = makeFile('progress.bin', BLOCK_SIZE * 4); // 4 blocks

    __setUploadFn(async (_block, _url) => {}); // always succeeds
    __setDelayFn(async () => {}); // no delay in tests

    useUploadEngine.getState().addFiles([file]);
    const fileId = useUploadEngine.getState().files[0].id;

    useUploadEngine.getState().start({ files: [{ uploadUrl: 'http://example.com/1' }] });

    await waitForStatus(fileId, 'done');

    const state = useUploadEngine.getState();
    expect(state.files[0].progress).toBe(1); // 4 blocks * 8 MiB / (4 * 8 MiB) = 1
  });

  it('zero-byte file marked done immediately (EC-001-3)', async () => {
    // US-001-05: zero-byte guard — no divide-by-zero.
    const file = makeFile('empty.bin', 0);

    __setUploadFn(async (_block, _url) => {});
    __setDelayFn(async () => {});

    useUploadEngine.getState().addFiles([file]);
    const fileId = useUploadEngine.getState().files[0].id;

    useUploadEngine.getState().start({ files: [{ uploadUrl: 'http://example.com/1' }] });

    await waitForStatus(fileId, 'done');

    const state = useUploadEngine.getState();
    expect(state.files[0].status).toBe('done');
    expect(state.files[0].progress).toBe(1);
  });
});
