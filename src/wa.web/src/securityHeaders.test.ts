import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

// CHORE-deploy-hardening (TA-9.5): nginx.conf is an envsubst template; read it directly (precedent touchTargets.test.ts).
const read = (rel: string) => readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8')
const nginx = read('../nginx.conf')
const indexHtml = read('../index.html')

const REQUIRED_HEADERS = [
  'Content-Security-Policy',
  'X-Content-Type-Options',
  'Referrer-Policy',
  'X-Frame-Options',
  'Permissions-Policy',
]

/** Returns the `location ... { ... }` blocks; `${VAR}` placeholders are neutralised first so their braces do not end a block. */
function locationBlocks(source: string): { name: string; body: string }[] {
  const flat = source.replace(/\$\{(\w+)\}/g, '<$1>')
  return [...flat.matchAll(/location\s+(\S+)\s*\{([^}]*)\}/g)].map((m) => ({ name: m[1], body: m[2] }))
}

describe('security headers contract (CHORE-deploy-hardening, TA-9.5)', () => {
  const blocks = locationBlocks(nginx)

  it('finds both locations', () => {
    expect(blocks.map((b) => b.name)).toEqual(['/', '/assets/'])
  })

  it.each(blocks.map((b) => [b.name, b.body] as const))(
    'location %s repeats all five headers with always (add_header inheritance trap)',
    (_name, body) => {
      for (const header of REQUIRED_HEADERS) {
        expect(body).toMatch(new RegExp(`add_header ${header} ".*" always;`))
      }
    },
  )

  it('CSP keeps the self-only script policy and the runtime origins', () => {
    const csp = nginx.match(/add_header Content-Security-Policy "([^"]*)"/)?.[1] ?? ''
    expect(csp).toContain("script-src 'self'")
    expect(csp).toContain("frame-ancestors 'none'")
    expect(csp).toContain('${WA_BLOB_ORIGIN}')
    expect(csp).toContain('${WA_API_ORIGIN}')
    expect(csp).toContain('https://dc.services.visualstudio.com')
  })

  it('index.html has no inline script (script-src self)', () => {
    const scripts = [...indexHtml.matchAll(/<script\b([^>]*)>/gi)]
    expect(scripts.length).toBeGreaterThan(0)
    for (const s of scripts) expect(s[1]).toMatch(/\bsrc=/)
  })
})
