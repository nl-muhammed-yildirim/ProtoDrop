import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

import { matchLinkPath } from './recipientRoute'

// T-003-02-fix (reviewer minor): cross-file drift guard — LinkId.cs is the sole source of truth for the
// minted charset. This test reads that file (not a retyped copy) and asserts every minted character routes
// to the active page, so RECIPIENT_PATH_RE can't silently fall out of sync with LinkId.Charset again.
const LINK_ID_CS = fileURLToPath(new URL('../../../../wa.domain/ValueObjects/LinkId.cs', import.meta.url))

function linkIdCsCharset(): string {
  const m = readFileSync(LINK_ID_CS, 'utf8').match(/public const string Charset = "([^"]+)"/)
  return m?.[1] ?? ''
}

describe('recipientRoute — LinkId charset drift guard (T-003-02-fix)', () => {
  it('matches /t/ paths for every character LinkId.cs mints (reviewer blocker: t and v were missing)', () => {
    const charset = linkIdCsCharset()
    expect(charset).not.toBe('') // the const is still greppable — if this fails, the parse changed; fix it here too.
    for (const c of charset) {
      const id = `${c}${'a'.repeat(7)}`
      expect(matchLinkPath(`/t/${id}`), `/t/${id}`).toBe(id) // 8-char linkIds, e.g. /t/tab3def5 — the reviewer's t/v blocker
    }
  })

  it('rejects every character outside the LinkId.cs charset (i/l/o/u + uppercase)', () => {
    const charset = new Set(linkIdCsCharset().split(''))
    for (const c of 'ilou'.concat('ABCDEFGHIJKLMNOPQRSTUVWXYZ')) {
      expect(charset.has(c)).toBe(false) // sanity: the C# side still excludes these; a true here means LinkId changed.
      expect(matchLinkPath(`/t/${c}${'a'.repeat(7)}`), `/${c}`).toBeNull() // e.g. /t/iab3def5g, /t/Aab3def5g
    }
  })
})
