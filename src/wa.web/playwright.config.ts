import { defineConfig, devices } from '@playwright/test';

// T-001-09: the local stack (API :8080, Vite :5173, SQL + Azurite) is launched externally —
// docker-compose.local.yml + supervised services — so no webServer key here.
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts', // Azurite ships with CORS off; set the Vite-origin rule before any test.
  timeout: 90_000, // AC-001-1 uploads ~2 GiB of sparse blocks to Azurite; AC-001-3 adds the full 7.5 s backoff schedule.
  expect: { timeout: 30_000 },
  workers: 1, // rows live in one page's module state — serial keeps drafts deterministic.
  fullyParallel: false,
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' }, testIgnore: /mobile\.spec\.ts/ },
    { name: 'mobile-iphone-14', use: { ...devices['iPhone 14'] }, testMatch: /mobile\.spec\.ts/ },
    {
      name: 'mobile-pixel-7',
      use: { ...devices['Pixel 7'], permissions: ['clipboard-read', 'clipboard-write'] },
      testMatch: /mobile\.spec\.ts/,
    },
  ],
});
