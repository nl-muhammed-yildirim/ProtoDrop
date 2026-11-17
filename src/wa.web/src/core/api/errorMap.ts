/**
 * T-013-02 (FR-013-6, TA-8.2): The code → screen/state map for Problem+JSON errors.
 * 
 * Parses the TA-4.1.3 wire shape into a typed ProblemError and maps each closed-list code
 * to its dedicated screen/state per F-TRF-013-errors.md's UI notes. Unknown/missing codes
 * fall back to the 500 screen (the only unmapped path).
 */

export interface ProblemError {
  code: string
  status: number
  correlationId?: string
  details?: string
}

/**
 * Parse a Problem+JSON response body into a typed ProblemError.
 * Returns null if the body is not valid Problem+JSON.
 */
export function parseProblem(body: unknown): ProblemError | null {
  if (typeof body !== 'object' || body === null) return null
  
  const obj = body as Record<string, unknown>
  if (typeof obj.code !== 'string') return null
  
  return {
    code: obj.code,
    status: typeof obj.status === 'number' ? obj.status : 0,
    correlationId: typeof obj.correlationId === 'string' ? obj.correlationId : undefined,
    details: typeof obj.details === 'string' ? obj.details : undefined,
  }
}

export type ErrorScreenKind =
  | { kind: 'error500' }
  | { kind: 'rateLimited' }
  | { kind: 'inlineLimit' }
  | { kind: 'ftrf003State'; which: 'notFound' | 'expired' | 'deleted' }
  | { kind: 'wrongPasswordShake' }
  | { kind: 'maxDownloadsUsed' }
  | { kind: 'filesGone' }

/**
 * Map a Problem+JSON code to its screen/state per F-TRF-013-errors.md's UI notes.
 * Exhaustive over all 18 closed-list codes (TA-4.1.3).
 */
export function mapProblem(code: string): ErrorScreenKind {
  switch (code) {
    // Inline limit screens (F-TRF-007) — NOT full error pages
    case 'TRANSFER_SIZE_EXCEEDED':
    case 'STORAGE_QUOTA_EXCEEDED':
      return { kind: 'inlineLimit' }
    
    // F-TRF-003 states — the real wire codes for terminal transfer links. Unknown links emit
    // TRANSFER_EXPIRED (404) per FR-003-9; NOT_FOUND is in the 500 bucket below (plan's explicit list).
    case 'TRANSFER_EXPIRED':
      return { kind: 'ftrf003State', which: 'expired' }
    case 'TRANSFER_DELETED':
      return { kind: 'ftrf003State', which: 'deleted' }
    
    // F-TRF-003-6: shake + "Wrong password"
    case 'WRONG_PASSWORD':
      return { kind: 'wrongPasswordShake' }
    
    // "All downloads have been used." screen
    case 'MAX_DOWNLOADS_REACHED':
      return { kind: 'maxDownloadsUsed' }
    
    // "Files were deleted — upload again." (F-TRF-010)
    case 'FILES_GONE':
      return { kind: 'filesGone' }
    
    // "A moment of calm, then try again." with 60s hint — NO Ref id
    case 'RATE_LIMITED':
      return { kind: 'rateLimited' }
    
    // Everything else / unknown code → 500 screen (the fallback is the only unmapped path)
    default:
      return { kind: 'error500' }
  }
}
