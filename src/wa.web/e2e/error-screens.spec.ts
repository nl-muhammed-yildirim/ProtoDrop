import { expect, test } from '@playwright/test'

/**
 * T-013-02 (AC-013-1 UI half): Error screens E2E — forced 500 via the T-013-01 chaos endpoint shape.
 *
 * The chaos endpoint GET /api/v1/dev/chaos?kind=internal returns a 500 Problem+JSON with code INTERNAL + correlationId.
 * This test intercepts the transfer endpoint to return the same shape, then verifies:
 * - The 500 screen renders with "Ref:" + 8-hex id + Try again
 * - Re-issue (Try again) works without a full reload
 */

test.describe('T-013-02 Error screens E2E', () => {

  // ---- AC-013-1: Forced 500 → screen shows "Ref:" + 8-hex id + Try again; re-issue works without full reload ----
  test('AC-013-1 — forced 500 shows Ref + Try again; re-issue works without full reload', async ({ page }) => {
    const CORRELATION_ID = 'a1b2c3d4' // 8-hex id (TA-4.1.3 wire shape)

    // Intercept the transfer endpoint to return a 500 Problem+JSON (same shape as chaos endpoint).
    let fetchCount = 0
    await page.route('**/api/v1/public/transfers/**', async (route) => {
      fetchCount++
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          code: 'INTERNAL',
          status: 500,
          correlationId: CORRELATION_ID,
          details: 'Internal server error',
        }),
      })
    })

    // Navigate to a recipient page.
    await page.goto('/t/testlink1')

    // Verify the 500 screen renders with Ref + Try again.
    const es = page.locator('.es')
    await expect(es).toBeVisible()
    await expect(es).toContainText('Something went wrong.')
    await expect(es).toContainText('Try again')
    await expect(es).toContainText(`Ref: ${CORRELATION_ID}`)

    // Record the initial fetch count.
    const initialFetchCount = fetchCount

    // Click Try again — should re-fetch without full reload.
    const tryAgainBtn = page.locator('.es-action')
    await tryAgainBtn.click()

    // Wait for the re-fetch to complete (the route will return 500 again).
    await expect(es).toBeVisible({ timeout: 5000 })

    // Verify a new fetch happened (re-issue worked).
    expect(fetchCount).toBeGreaterThan(initialFetchCount)

    // Verify no full reload happened — the page URL is still the same.
    expect(page.url()).toContain('/t/testlink1')
  })

  // ---- AC-013-4: SPA 404 → unknown route shows "Oops — this page moved." + Go to home, NO Ref line ----
  test('AC-013-4 — unknown route shows 404 screen with Go to home, no Ref line', async ({ page }) => {
    // Navigate to an unknown route.
    await page.goto('/nonexistent-route')

    // Verify the 404 screen renders.
    const es = page.locator('.es')
    await expect(es).toBeVisible()
    await expect(es).toContainText('Oops — this page moved.')
    await expect(es).toContainText('Go to home')

    // No Ref line (no API request happened for an unknown SPA route).
    await expect(es).not.toContainText('Ref:')
  })
})
