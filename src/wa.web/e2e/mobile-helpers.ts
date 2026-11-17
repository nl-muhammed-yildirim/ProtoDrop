import { expect } from '@playwright/test'
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test'
import crypto from 'node:crypto'
import zlib from 'node:zlib'

import { API_BASE } from './seed-transfer'

/**
 * T-017-04 (AC-017-3, EC-017-3): shared assertions for the mobile matrix (e2e/mobile.spec.ts).
 * One call per screen: layout/tap-target sweep, sticky-bar clearance, scalable viewport meta, 200 % zoom safety.
 * Test accounts are @example.com only (TA-14.3) and exist in the local dev DB only.
 */

/** Min tap target edge in CSS px (T-017-03 / FR-017-3) and min input font size (iOS no-zoom-on-focus). */
const MIN_TARGET_PX = 44
const MIN_INPUT_FONT_PX = 16
const SUBPIXEL_TOLERANCE = 0.5

const INTERACTIVE = 'a[href], button, input:not([type="hidden"]), select, textarea, [role="button"], [tabindex]:not([tabindex="-1"])'

/**
 * AC-017-3: no horizontal scroll at the current viewport, and every visible interactive element is a >= 44x44 target
 * (inputs/selects/textareas also >= 16 px font). "Visible" excludes display:none / visibility:hidden, boxes <= 1 px in
 * either axis (sr-only clip) and boxes entirely outside the viewport horizontally.
 */
export async function expectPhoneLayout(page: Page, screen: string): Promise<void> {
  const result = await page.evaluate(
    ({ selector, minTarget, minFont, tol }) => {
      const iw = window.innerWidth
      const offenders: string[] = []
      for (const el of Array.from(document.querySelectorAll<HTMLElement>(selector))) {
        const cs = getComputedStyle(el)
        if (cs.display === 'none' || cs.visibility === 'hidden') continue
        const r = el.getBoundingClientRect()
        if (r.width <= 1 || r.height <= 1) continue
        if (r.right <= 0 || r.left >= iw) continue
        const label = `${el.tagName.toLowerCase()}.${String(el.className).trim().replace(/\s+/g, '.')} ${Math.round(r.width * 10) / 10}x${Math.round(r.height * 10) / 10}`
        if (r.width + tol < minTarget || r.height + tol < minTarget) offenders.push(`target<44: ${label}`)
        if (/^(input|select|textarea)$/i.test(el.tagName) && parseFloat(cs.fontSize) + tol < minFont) {
          offenders.push(`font<16: ${label} (${cs.fontSize})`)
        }
      }
      return { scrollWidth: document.documentElement.scrollWidth, innerWidth: iw, offenders }
    },
    { selector: INTERACTIVE, minTarget: MIN_TARGET_PX, minFont: MIN_INPUT_FONT_PX, tol: SUBPIXEL_TOLERANCE },
  )
  expect(result.scrollWidth, `${screen}: no horizontal scroll`).toBeLessThanOrEqual(result.innerWidth)
  expect(result.offenders, `${screen}: tap-target / input font offenders`).toEqual([])
}

/** Scrolls to the bottom and asserts the last row's bottom edge sits above the fixed bar's top (reserve is enough). */
export async function expectStickyClear(page: Page, lastSelector: string, barSelector: string): Promise<void> {
  const last = page.locator(lastSelector).last()
  await last.scrollIntoViewIfNeeded()
  await page.evaluate(() => {
    for (const el of [document.scrollingElement, document.querySelector('main')]) if (el) el.scrollTop = el.scrollHeight
  })
  const lastBox = await last.boundingBox()
  const barBox = await page.locator(barSelector).first().boundingBox()
  expect(lastBox, `${lastSelector} has a box`).not.toBeNull()
  expect(barBox, `${barSelector} has a box`).not.toBeNull()
  expect(lastBox!.y + lastBox!.height, `${lastSelector} bottom clears ${barSelector} top`).toBeLessThanOrEqual(barBox!.y + SUBPIXEL_TOLERANCE)
}

/** EC-017-3: the viewport meta must not disable pinch-zoom. */
export async function expectViewportMetaScalable(page: Page): Promise<void> {
  const content = await page.locator('meta[name="viewport"]').getAttribute('content')
  expect(content, 'viewport meta present').not.toBeNull()
  expect(content).toContain('width=device-width')
  expect(content).not.toMatch(/user-scalable\s*=\s*(no|0)/i)
  expect(content).not.toMatch(/maximum-scale\s*=\s*1(\.0*)?\b/i)
}

/**
 * EC-017-3: 200 % zoom == the same page laid out in half the CSS width. Halves the viewport, asserts no horizontal
 * scroll and selectable text, then restores the original size.
 */
export async function expectZoomSafe(page: Page, screen: string): Promise<void> {
  const original = page.viewportSize()!
  await page.setViewportSize({ width: Math.floor(original.width / 2), height: original.height })
  try {
    const m = await page.evaluate(() => {
      const h1 = document.querySelector('h1')
      return {
        sw: document.documentElement.scrollWidth,
        iw: window.innerWidth,
        bodySelect: getComputedStyle(document.body).userSelect,
        h1Select: h1 ? getComputedStyle(h1).userSelect : 'auto',
      }
    })
    expect(m.sw, `${screen}: no horizontal scroll at 200% zoom`).toBeLessThanOrEqual(m.iw)
    expect(m.bodySelect, `${screen}: body text selectable`).not.toBe('none')
    expect(m.h1Select, `${screen}: heading text selectable`).not.toBe('none')
  } finally {
    await page.setViewportSize(original)
  }
}

/** Stored (no compression) zip with UTF-8 names (general-purpose flag bit 11). Enough for a byte-identity download check. */
export function buildStoredZip(entries: Array<{ name: string; data: Buffer }>): Buffer {
  const locals: Buffer[] = []
  const centrals: Buffer[] = []
  let offset = 0
  for (const e of entries) {
    const name = Buffer.from(e.name, 'utf8')
    const crc = zlib.crc32(e.data) >>> 0
    const lh = Buffer.alloc(30)
    lh.writeUInt32LE(0x04034b50, 0); lh.writeUInt16LE(20, 4); lh.writeUInt16LE(0x0800, 6); lh.writeUInt16LE(0, 8)
    lh.writeUInt16LE(0, 10); lh.writeUInt16LE(0x21, 12) // 00:00, 1980-01-01
    lh.writeUInt32LE(crc, 14); lh.writeUInt32LE(e.data.length, 18); lh.writeUInt32LE(e.data.length, 22)
    lh.writeUInt16LE(name.length, 26); lh.writeUInt16LE(0, 28)
    const ch = Buffer.alloc(46)
    ch.writeUInt32LE(0x02014b50, 0); ch.writeUInt16LE(20, 4); ch.writeUInt16LE(20, 6); ch.writeUInt16LE(0x0800, 8)
    ch.writeUInt16LE(0, 10); ch.writeUInt16LE(0, 12); ch.writeUInt16LE(0x21, 14)
    ch.writeUInt32LE(crc, 16); ch.writeUInt32LE(e.data.length, 20); ch.writeUInt32LE(e.data.length, 24)
    ch.writeUInt16LE(name.length, 28) // extra, comment, disk, attrs stay 0
    ch.writeUInt32LE(offset, 42)
    locals.push(lh, name, e.data)
    centrals.push(ch, name)
    offset += lh.length + name.length + e.data.length
  }
  const cd = Buffer.concat(centrals)
  const eocd = Buffer.alloc(22)
  eocd.writeUInt32LE(0x06054b50, 0); eocd.writeUInt16LE(entries.length, 8); eocd.writeUInt16LE(entries.length, 10)
  eocd.writeUInt32LE(cd.length, 12); eocd.writeUInt32LE(offset, 16)
  return Buffer.concat([...locals, cd, eocd])
}

/**
 * Signs up a throwaway @example.com account against the local API and installs the session in the page's context
 * (wa.session cookie for the API + the client's localStorage mirror 'protoDrop.session'). Signup's wire shape is the
 * flat profile DTO + sessionToken (T-008-01) and sets no cookie.
 */
export async function seedSignedInSession(
  page: Page,
  context: BrowserContext,
  request: APIRequestContext,
): Promise<{ userId: string; email: string }> {
  const email = `mobile-e2e-${crypto.randomUUID()}@example.com`
  const password = `Pw-${crypto.randomUUID()}`
  const res = await request.post(`${API_BASE}/api/v1/auth/signup`, { data: { email, password } })
  if (res.status() >= 300) throw new Error(`seed: signup failed — ${res.status()} ${await res.text()}`)
  const body = (await res.json()) as { sessionToken: string; userId: string; email: string; displayName: string; theme: string; planCode: string }
  const stored = { userId: body.userId, email: body.email, displayName: body.displayName, theme: body.theme, planCode: body.planCode }
  await context.addCookies([{ name: 'wa.session', value: body.sessionToken, url: API_BASE, httpOnly: true, sameSite: 'Lax' }])
  await page.addInitScript((profile) => {
    window.localStorage.setItem('protoDrop.session', JSON.stringify(profile))
  }, stored)
  return { userId: stored.userId, email: stored.email }
}
