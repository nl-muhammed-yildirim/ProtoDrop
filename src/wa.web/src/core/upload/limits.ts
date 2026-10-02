// TA-3.4 / F-TRF-007 — Limits Registry client-side resolution.
// In production this will be replaced by a fetch to `/api/v1/limits` when auth lands.

// Field names mirror the server LimitsRecord (wa.domain/LimitsRecord.cs, TA-3.4) — keep in sync.
export interface LimitsRecord {
  maxTransferSize: number; // bytes (TA-3.4)
  maxSingleFile: number;   // bytes (TA-3.4)
  storageQuota: number;    // bytes (TA-3.4) — server field name is "storageQuota" (LimitsFree seed)
  maxEmails: number;       // TA-3.4 — max recipients per send (EC-006-1)
}

/** A single selection violation. `T` narrows the discriminated union per call site (e.g. Violation<'singleFile'>). */
export type Violation<T extends 'total' | 'singleFile'> = { type: T; size: number; limit: number };

/**
 * Resolve the effective limits for the current user/plan.
 * For guests, returns Free plan defaults as defined in the Limits Registry.
 * In production this will be replaced by a fetch to `/api/v1/limits`.
 */
export function resolveEffectiveLimits(): LimitsRecord {
  // Guest / free tier defaults (matches seeded values in PlanConfiguration.cs):
  const freeDefaults: LimitsRecord = {
    maxTransferSize: 5 * 1024 ** 3,   // 5 GB
    maxSingleFile: 5 * 1024 ** 3,     // 5 GB
    storageQuota: 5 * 1024 ** 3,      // 5 GB (free tier)
    maxEmails: 20,
  };

  return freeDefaults;
}

/**
 * Validate a selection of files against the given limits.
 * Returns an array of violations — each violation describes which limit was exceeded and by how much.
 */
export function validateSelection(files: { size: number }[], limits: LimitsRecord): Array<Violation<'total' | 'singleFile'>> {
  const totalBytes = files.reduce((sum, file) => sum + file.size, 0);

  const violations: Array<Violation<'total' | 'singleFile'>> = [];

  if (totalBytes > limits.maxTransferSize) {
    violations.push({ type: 'total', size: totalBytes, limit: limits.maxTransferSize });
  }

  for (const file of files) {
    if (file.size > limits.maxSingleFile) {
      violations.push({ type: 'singleFile', size: file.size, limit: limits.maxSingleFile });
    }
  }

  return violations;
}
