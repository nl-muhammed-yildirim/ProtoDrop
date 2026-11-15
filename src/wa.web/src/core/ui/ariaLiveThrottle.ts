/**
 * T-016-01 (FR-016-3 / EC-016-1): upload progress announcement throttle + failure-batch summary.
 * Pure module — NO React import; the useAriaLive hook consumes these. Announcements happen only at
 * 0/25/50/75/100 (EC-016-1) so screen readers are not flooded with intermediate ticks.
 */

/** The exact announcement points pinned by EC-016-1 — the throttle announces only when one of these is crossed. */
export const PROGRESS_POINTS = [0, 25, 50, 75, 100] as const

// Interior milestones: the endpoints are handled separately (>=100 always announces; a run starts at exactly 0).
const MILESTONES = [25, 50, 75] as const

/**
 * Announces only when an EC-016-1 point is crossed. Returns the announced percent or null for silent ticks.
 * A fresh throttle announces on its first tick ONLY at exactly 0 (rule b) — milestone crossings require a
 * prior announcement, so upload runs must start with their initial 0 report to get milestone announcements;
 * completion (>=100) is always announced and resets the run so the next upload re-announces from 0.
 */
export class ProgressThrottle {
  private lastAnnounced: number | null = null

  onProgress(percent: number): number | null {
    if (percent >= 100) { this.lastAnnounced = null; return percent } // always announced; reset for the next run.
    const last = this.lastAnnounced
    if (last === null && percent === 0) { this.lastAnnounced = 0; return 0 } // start-of-run announcement, once per run.
    for (const p of MILESTONES) {
      if (last !== null && last < p && p <= percent) { this.lastAnnounced = percent; return percent }
    }
    return null
  }

  /** Drops the current run — the next upload re-announces from its own initial tick. */
  reset(): void { this.lastAnnounced = null }
}

/**
 * Collapses a batch of per-file failure messages into exactly ONE count-bearing summary line (US-016-02 alt flow).
 * Empty batch → null (nothing to announce); non-empty → one plain English line with the numeric count embedded.
 * Exact copy/i18n is T-016-02's job — this shape (one line, carries the count) is what that task fixes in place.
 */
export function summarizeFailures(messages: string[]): string | null {
  if (messages.length === 0) return null
  return `${messages.length} files failed`
}
