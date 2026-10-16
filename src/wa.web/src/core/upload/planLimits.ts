import { formatBytes } from './formatBytes'

/**
 * Effective plan limits the client pre-checks against (T-001-04).
 *
 * The server re-check at draft creation (T-001-05) is the rule; this module is the
 * UX pre-flight's source. `PlanLimits` is the subset of the C# `LimitsRecord`
 * (TA-3.4, same camelCase keys) that FR-001-3 needs. Guests are evaluated against
 * the Free plan by default (US-001-03), so the module defaults to Free — callers
 * never hardcode a limit; `setPlanLimits()` swaps in a real per-user source when
 * T-001-05 wires one, and this single home is where that value lives meanwhile.
 */

export interface PlanLimits {
  /** MAX_TRANSFER_SIZE, bytes (LimitsRecord.maxTransferSize). */
  maxTransferSize: number
  /** MAX_SINGLE_FILE, bytes (LimitsRecord.maxSingleFile). */
  maxSingleFile: number
}

/** Free-plan seed (Part 2 / LimitsRecord): guests resolve to "free" by default. */
const FREE_LIMITS: PlanLimits = {
  // D-06 default-accepted (Open-Decisions Part 1) — mirrors the seeded LimitsJson.
  maxTransferSize: 5368709120, // MAX_TRANSFER_SIZE free (TA-3.4 Part 2).
  maxSingleFile: 5368709120,   // MAX_SINGLE_FILE free (= transfer size for all plans).
}

let limits = FREE_LIMITS

/** Swap the effective limits in place (tests; T-001-05's real wiring). */
export function setPlanLimits(next: PlanLimits): void {
  limits = next
}

/** Restore the Free guest defaults (test cleanup). */
export function resetPlanLimits(): void {
  limits = FREE_LIMITS
}

export function getPlanLimits(): PlanLimits {
  return limits
}

/** Structural shape so this module does not import the UploadEngine store. */
type Sizable = { readonly id: string; readonly size: number }

export interface LimitCheckResult {
  /** Total staged bytes exceed maxTransferSize. */
  overTotal: boolean
  /** Ids of staged rows whose own size exceeds maxSingleFile (marked). */
  overFileIds: Set<string>
}

/**
 * Pure arithmetic on staged sizes — no file contents read, so it stays fast on
 * mobile (TA-8.3). Re-run after every staging change; the result drives both the
 * message under the total line and the per-file row marking.
 */
export function checkLimits(files: readonly Sizable[], plan: PlanLimits): LimitCheckResult {
  let total = 0
  const overFileIds = new Set<string>()
  for (const f of files) {
    total += f.size
    if (f.size > plan.maxSingleFile) overFileIds.add(f.id)
  }
  return { overTotal: total > plan.maxTransferSize, overFileIds }
}

/** Copy for the line under the total; names the exact limit via formatBytes(). */
export function limitMessage(files: readonly Sizable[], result: LimitCheckResult, plan: PlanLimits): string | null {
  const n = result.overFileIds.size
  if (n > 0) {
    // A marked file also pushes the total over; per-file is the actionable one —
    // removing the marked rows clears both, so this takes precedence.
    const limit = formatBytes(plan.maxSingleFile)
    return n === 1
      ? `This file exceeds the ${limit} per-file limit. Remove it to continue.`
      : `${n} files exceed the ${limit} per-file limit. Remove them to continue.`
  }
  if (result.overTotal) {
    const total = formatBytes(files.reduce((sum, f) => sum + f.size, 0))
    return `Transfer size ${total} exceeds the ${formatBytes(plan.maxTransferSize)} limit. Remove files to continue.`
  }
  return null
}
