import { create } from 'zustand'

/**
 * T-001-10 handoff (shared): the finalized transfer result + error flag, lifted out of StagingList so App can hide
 * the DropZone once the link screen is showing — otherwise the upload area renders above LinkScreen on landing.
 */

/** Finalize response wire shape — mirrors Program.cs L229 exactly (id in 'n'-form, status "Draft"). */
export interface FinalizeResult {
  id: string
  linkId: string
  status: string
  expiresAt: string
  totalBytes: number
  files: Array<{ id: string; name: string }>
}

interface FinalizeState {
  finalized: FinalizeResult | null
  finalizeError: boolean
  setFinalized: (r: FinalizeResult) => void
  setFinalizeError: (e: boolean) => void
  clear: () => void
}

export const useFinalize = create<FinalizeState>((set) => ({
  finalized: null,
  finalizeError: false,
  setFinalized: (r) => set({ finalized: r, finalizeError: false }),
  setFinalizeError: (e) => set({ finalizeError: e }),
  clear: () => set({ finalized: null, finalizeError: false }),
}))
