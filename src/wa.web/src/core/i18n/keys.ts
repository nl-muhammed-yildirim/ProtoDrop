/**
 * TA-8.5 {area}.{screen} pattern — English values verbatim from spec (T-026 will localize).
 * T-013-04 (FR-013-3): degraded-storage row states copy — shared between upload and download surfaces.
 */
export const DEGRADED_COPY = {
  /** AC-013-2 block 1: upload row after blob 503 retries exhausted. */
  uploadDegraded: 'Storage temporarily unavailable — retry.',
  /** AC-013-2 block 2: download row when SAS mint fails with 503+INTERNAL. */
  downloadUnavailable: 'File temporarily unavailable.',
} as const
