import { defineConfig, devices } from '@playwright/test';

// T-001-09: the local stack (API :8080, Vite :5173, SQL + Azurite) is launched externally —
// docker-compose.local.yml + supervised services — so no webServer key here.
//
// T-028: E2E_BASE_URL switches the suite to a deployed URL. Unset → the local projects (unchanged) plus the
// `staging-*` projects (every remote test skips itself without E2E_BASE_URL). Set → ONLY the `staging-*` projects:
// the local specs need direct SQL + Azurite, which a deployed environment does not expose.
const remoteBaseUrl = process.env.E2E_BASE_URL?.trim().replace(/\/+$/, '') || undefined;
// The Azurite CORS globalSetup is only meaningful for a local stack; in CD it would fail reaching 127.0.0.1:10000.
const localHost = !remoteBaseUrl || /^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(remoteBaseUrl);
const REMOTE_DIR = /[\\/]remote[\\/]/;

const localProjects = [
  { name: 'chromium', use: { browserName: 'chromium' as const }, testIgnore: [/mobile\.spec\.ts/, REMOTE_DIR] },
  { name: 'mobile-iphone-14', use: { ...devices['iPhone 14'] }, testMatch: /mobile\.spec\.ts/ },
  {
    name: 'mobile-pixel-7',
    use: { ...devices['Pixel 7'], permissions: ['clipboard-read', 'clipboard-write'] },
    testMatch: /mobile\.spec\.ts/,
  },
];

// D11: a Playwright trace records API request headers, i.e. the session cookie — and Actions artifacts are downloadable.
const stagingUse = { browserName: 'chromium' as const, trace: 'off' as const, screenshot: 'only-on-failure' as const };
const stagingProjects = [
  { name: 'staging-setup', testMatch: /remote[\\/]auth\.setup\.ts/, use: stagingUse, teardown: 'staging-teardown' },
  { name: 'staging-teardown', testMatch: /remote[\\/]auth\.teardown\.ts/, use: stagingUse },
  { name: 'staging', testMatch: /remote[\\/].*\.spec\.ts/, dependencies: ['staging-setup'], use: stagingUse, retries: 0 },
];

export default defineConfig({
  testDir: './e2e',
  globalSetup: localHost ? './e2e/global-setup.ts' : undefined, // Azurite ships with CORS off; set the Vite-origin rule before any test.
  timeout: 90_000, // AC-001-1 uploads ~2 GiB of sparse blocks to Azurite; AC-001-3 adds the full 7.5 s backoff schedule.
  expect: { timeout: 30_000 },
  workers: 1, // rows live in one page's module state — serial keeps drafts deterministic.
  fullyParallel: false,
  use: {
    baseURL: remoteBaseUrl ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: remoteBaseUrl ? stagingProjects : [...localProjects, ...stagingProjects],
});
