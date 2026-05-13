import { defineConfig, devices } from '@playwright/test';

// Port for the test server. Override with PLAYWRIGHT_PORT to avoid clashing
// with a running dev instance.
const PORT = Number(process.env.PLAYWRIGHT_PORT ?? 5099);
const BASE_URL = `http://127.0.0.1:${PORT}`;

export default defineConfig({
  testDir: './specs',
  // Each spec is fully self-contained (registers its own user, creates its own
  // repo) so they can run in parallel without colliding. The smoke suite is
  // tiny — keep it tight.
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // Playwright's getByRole/getByLabel/getByText pierce shadow DOM by default,
    // which is what we need for Lit components. No special selector engine.
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: `node scripts/start-test-server.mjs`,
    url: `${BASE_URL}/healthz`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    stdout: 'pipe',
    stderr: 'pipe',
    env: {
      PLAYWRIGHT_PORT: String(PORT),
    },
  },
});
